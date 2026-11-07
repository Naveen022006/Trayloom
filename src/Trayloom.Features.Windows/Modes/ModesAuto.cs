// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>One automatic start: when an app comes to the front, or at a time of day, enter a mode (optionally leaving it again later).</summary>
internal sealed class ModeRule
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The mode to enter.</summary>
    public string ModeId { get; set; } = string.Empty;

    /// <summary><c>app</c> or <c>time</c>.</summary>
    public string Kind { get; set; } = "app";

    /// <summary>App rules: the executable's file name only, lower case (<c>code.exe</c>). Never a path.</summary>
    public string App { get; set; } = string.Empty;

    /// <summary>App rules: the name shown to the person (the file description at the time the rule was saved); may be empty.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Time rules: the start time as <c>H:mm</c> or <c>HH:mm</c>.</summary>
    public string At { get; set; } = "09:00";

    /// <summary>Time rules: optional time to leave the mode again; empty for never. Must be later than <see cref="At"/>.</summary>
    public string Until { get; set; } = string.Empty;

    /// <summary>Time rules: the days it applies to; bit 0 is Monday and bit 6 is Sunday. Weekdays by default.</summary>
    public int Days { get; set; } = 0b0011111;

    /// <summary>True to show a question in the island instead of entering the mode right away.</summary>
    public bool Ask { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public ModeRule Clone() => (ModeRule)MemberwiseClone();
}

/// <summary>The content of <c>rules.json</c>.</summary>
internal sealed class ModeRulesFile
{
    public int Version { get; set; } = 1;

    public List<ModeRule> Rules { get; set; } = [];
}

/// <summary>The setting keys of the automatic starts. Registered by the Modes feature.</summary>
internal static class ModesAutoKeys
{
    /// <summary>The master switch.</summary>
    public const string Enabled = "modesAutoEnabled";

    /// <summary>Whether the closed island shows the active mode when nothing else is going on.</summary>
    public const string ShowInIsland = "modesShowInIsland";

    /// <summary>The id of the rule that started the active mode; empty when the person started it or none is active.</summary>
    public const string StartedBy = "modesAutoStartedBy";

    /// <summary>JSON object <c>{ruleId: "yyyy-MM-dd"}</c>: the day each time rule last fired or was skipped, so it fires once a day.</summary>
    public const string Fired = "modesAutoFired";

    public static void Register(IAppSettings app)
    {
        app.Register(Enabled, true);
        app.Register(ShowInIsland, true);
        app.Register(StartedBy, string.Empty);
        app.Register(Fired, string.Empty);
    }
}

/// <summary>What a rule wants to do once its trigger fired.</summary>
internal enum AutoDecision
{
    Skip,
    Ask,
    Enter,
}

/// <summary>The pure rules of the automatic starts, kept free of windows and processes so the harness can check them.</summary>
internal static class ModesAutoMath
{
    /// <summary>A time rule still fires this long after its time, so a PC that was locked or asleep at 09:00 starts the mode when it is back.</summary>
    public const int GraceMinutes = 30;

    /// <summary>The most rules a person can have.</summary>
    public const int MaxRules = 24;

    /// <summary>The executable name every comparison uses: <c>Code</c> and <c>C:\…\Code.exe</c> both become <c>code.exe</c>; empty when there is none.</summary>
    public static string NormalizeExe(string? exeOrName)
    {
        var text = exeOrName?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var slash = text.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }

        text = text.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return text.EndsWith(".exe", StringComparison.Ordinal) ? text : text + ".exe";
    }

    /// <summary>True for an enabled app rule whose executable is <paramref name="exeName"/>.</summary>
    public static bool AppMatches(ModeRule r, string exeName)
    {
        if (!r.Enabled || r.Kind != "app")
        {
            return false;
        }

        var rule = NormalizeExe(r.App);
        return rule.Length > 0 && rule == NormalizeExe(exeName);
    }

    /// <summary>Parses <c>H:mm</c> or <c>HH:mm</c>, 00:00 to 23:59.</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        var parts = text?.Trim().Split(':');
        if (parts is not { Length: 2 } || parts[0].Length is < 1 or > 2 || parts[1].Length != 2)
        {
            return false;
        }

        if (!parts[0].All(char.IsAsciiDigit) || !parts[1].All(char.IsAsciiDigit))
        {
            return false;
        }

        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (hours > 23 || minutes > 59)
        {
            return false;
        }

        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    /// <summary>The bit of a day in <see cref="ModeRule.Days"/>: Monday is bit 0, Sunday is bit 6.</summary>
    public static int DayBit(DayOfWeek day) => 1 << (((int)day + 6) % 7);

    public static bool DayOn(int days, DayOfWeek d) => (days & DayBit(d)) != 0;

    /// <summary>The key a day is stored under in <see cref="ModesAutoKeys.Fired"/>.</summary>
    public static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True when an enabled time rule should start now: its day is on, the time has come, the grace period has not run out and it did not fire today.</summary>
    public static bool TimeDue(ModeRule r, DateTime now, string? lastFiredDay)
    {
        if (!r.Enabled || r.Kind != "time" || !TryParseTime(r.At, out var at) || !DayOn(r.Days, now.DayOfWeek))
        {
            return false;
        }

        var start = now.Date + at;
        return now >= start && now < start.AddMinutes(GraceMinutes) && lastFiredDay != DayKey(now);
    }

    /// <summary>True when a time rule with a "Leave at" time should leave its mode now (same window as <see cref="TimeDue"/>).</summary>
    public static bool UntilDue(ModeRule r, DateTime now)
    {
        if (!r.Enabled || r.Kind != "time" || string.IsNullOrWhiteSpace(r.Until) || !DayOn(r.Days, now.DayOfWeek))
        {
            return false;
        }

        if (!TryParseTime(r.At, out var at) || !TryParseTime(r.Until, out var until) || until <= at)
        {
            return false;
        }

        var leave = now.Date + until;
        return now >= leave && now < leave.AddMinutes(GraceMinutes);
    }

    /// <summary>A rule never replaces a mode the person is already in, and never starts a mode that was deleted.</summary>
    public static AutoDecision Decide(ModeRule r, string? activeModeId, bool modeExists)
    {
        if (!modeExists || !string.IsNullOrEmpty(activeModeId))
        {
            return AutoDecision.Skip;
        }

        return r.Ask ? AutoDecision.Ask : AutoDecision.Enter;
    }

    /// <summary>"Every day", "Weekdays", "Weekends", "No days" or the short day names, Monday first.</summary>
    public static string DaysText(int days)
    {
        days &= 0b1111111;
        switch (days)
        {
            case 0b1111111:
                return ModesAutoStrings.EveryDay;
            case 0b0011111:
                return ModesAutoStrings.Weekdays;
            case 0b1100000:
                return ModesAutoStrings.Weekends;
            case 0:
                return ModesAutoStrings.NoDays;
        }

        var names = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            if ((days & (1 << i)) != 0)
            {
                names.Add(ModesAutoStrings.DayShort(i));
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>A rule list that is safe to use: unknown kinds, bad times and rules without a mode are dropped, the count is capped and every rule has a unique id.</summary>
    public static ModeRulesFile Sanitize(ModeRulesFile? f)
    {
        var clean = new ModeRulesFile();
        if (f?.Rules is null)
        {
            return clean;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in f.Rules)
        {
            if (source is null || clean.Rules.Count >= MaxRules)
            {
                continue;
            }

            var rule = source.Clone();
            if (rule.Kind is not ("app" or "time") || string.IsNullOrWhiteSpace(rule.ModeId))
            {
                continue;
            }

            rule.Days &= 0b1111111;
            if (rule.Kind == "app")
            {
                rule.App = NormalizeExe(rule.App);
                if (rule.App.Length == 0)
                {
                    continue;
                }

                rule.AppName = (rule.AppName ?? string.Empty).Trim();
                rule.Until = string.Empty;
            }
            else
            {
                if (!TryParseTime(rule.At, out var at))
                {
                    continue;
                }

                rule.At = FormatTime(at);
                if (string.IsNullOrWhiteSpace(rule.Until) || !TryParseTime(rule.Until, out var until) || until <= at)
                {
                    rule.Until = string.Empty;
                }
                else
                {
                    rule.Until = FormatTime(until);
                }

                rule.App = string.Empty;
                rule.AppName = string.Empty;
            }

            if (rule.AppName.Length > 60)
            {
                rule.AppName = rule.AppName[..60];
            }

            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
            {
                do
                {
                    rule.Id = NewRuleId();
                }
                while (!ids.Add(rule.Id));
            }

            clean.Rules.Add(rule);
        }

        return clean;
    }

    /// <summary>A new short rule id.</summary>
    public static string NewRuleId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>The time as <c>HH:mm</c>.</summary>
    public static string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}

/// <summary><c>rules.json</c> beside <c>modes.json</c>: exe file names and times only. A file that cannot be read is kept aside as <c>rules.json.bad</c>.</summary>
internal sealed class ModesRulesStore(string dataDir, ILog log)
{
    public const string FileName = "rules.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string FilePath => Path.Combine(dataDir, FileName);

    /// <summary>The saved rules, sanitised; empty when there is no file or it could not be read.</summary>
    public ModeRulesFile Load()
    {
        if (!File.Exists(FilePath))
        {
            return new ModeRulesFile();
        }

        try
        {
            var text = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize<ModeRulesFile>(text, Json) ?? throw new JsonException("empty document");
            return ModesAutoMath.Sanitize(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                File.Move(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch (Exception)
            {
                // the log line below still tells the person what happened
            }

            log.Warn("[modes] rules.json unreadable: kept aside");
            return new ModeRulesFile();
        }
    }

    /// <summary>Writes the file atomically. False when it could not be written.</summary>
    public bool Save(ModeRulesFile file)
    {
        try
        {
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(ModesAutoMath.Sanitize(file), Json));
            return true;
        }
        catch (Exception ex)
        {
            log.Error("[modes] rules.json could not be written", ex);
            return false;
        }
    }
}
