// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json.Serialization;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>Settings keys of Screen time (spec 06 section 1.2) and their defaults.</summary>
internal static class ScreenTimeKeys
{
    public const string FeatureId = "notch-screen-time";
    public const string ModuleId = "screenTime";
    public const string Enabled = "notchScreenTimeEnabled";
    public const string IdleMinutes = "notchScreenTimeIdleMinutes";
    public const string CountMedia = "notchScreenTimeCountMedia";
    public const string KeepDays = "notchScreenTimeKeepDays";

    /// <summary>The island's idle-content key (owned by the Island project; read only).</summary>
    public const string IdleContent = "notchIdleContent";

    /// <summary>Pseudo app id of the shell (desktop, taskbar, Start, Search).</summary>
    public const string Shell = "#shell";

    /// <summary>Pseudo app id of windows whose process cannot be read (elevated apps).</summary>
    public const string Unknown = "#unknown";

    /// <summary>Registers the defaults; safe to call repeatedly.</summary>
    public static void Register(IAppSettings app)
    {
        app.Register(Enabled, true);
        app.Register(IdleMinutes, 2);
        app.Register(CountMedia, true);
        app.Register(KeepDays, 7);
    }
}

/// <summary>One app's seconds in a day.</summary>
internal sealed class AppUsage
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public double Seconds { get; set; }
}

/// <summary>The in-memory model of one local calendar day.</summary>
internal sealed class DayUsage
{
    public DayUsage(DateTime date) => Date = date.Date;

    public DateTime Date { get; }

    /// <summary>Local time of the first counted second, "HH:mm" (null while nothing was counted).</summary>
    public string? First { get; set; }

    public double[] Hours { get; } = new double[24];

    public Dictionary<string, AppUsage> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seconds in total (the sum of the apps).</summary>
    public double Total => Apps.Values.Sum(a => a.Seconds);

    /// <summary>The apps, most used first.</summary>
    public List<AppUsage> Ranked() => [.. Apps.Values.Where(a => a.Seconds >= 0.5).OrderByDescending(a => a.Seconds)];

    /// <summary>Adds <paramref name="seconds"/> to an app and to the hour bucket.</summary>
    public void Add(string id, string name, double seconds, int hour)
    {
        if (!Apps.TryGetValue(id, out var app))
        {
            app = new AppUsage { Id = id, Name = name };
            Apps[id] = app;
        }

        app.Seconds += seconds;
        Hours[Math.Clamp(hour, 0, 23)] += seconds;
    }
}

/// <summary>The on-disk shape of a day (spec 1.4); the last three members only appear in the dev dump.</summary>
internal sealed class DayFile
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("first")]
    public string? First { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("hours")]
    public int[] Hours { get; set; } = new int[24];

    [JsonPropertyName("apps")]
    public List<AppFile> Apps { get; set; } = [];

    [JsonPropertyName("tracking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Tracking { get; set; }

    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Active { get; set; }

    [JsonPropertyName("activeReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveReason { get; set; }
}

/// <summary>One app of a day file.</summary>
internal sealed class AppFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("seconds")]
    public int Seconds { get; set; }
}

/// <summary>Duration and time formatting (spec 1.5).</summary>
internal static class ScreenTimeFormat
{
    /// <summary>"3 h 5 min", "42 min" or "Less than a minute".</summary>
    public static string Long(double seconds)
    {
        var total = (int)Math.Floor(seconds);
        if (total < 60)
        {
            return ScreenTimeStrings.LessThanAMinute;
        }

        var h = total / 3600;
        var m = total % 3600 / 60;
        return h > 0 ? Loc.F("{0} h {1} min", h, m) : Loc.F("{0} min", m);
    }

    /// <summary>"1h 05m", "12m" or "&lt;1m".</summary>
    public static string Compact(double seconds)
    {
        var total = (int)Math.Floor(seconds);
        if (total < 60)
        {
            return Loc.T("<1m");
        }

        var h = total / 3600;
        var m = total % 3600 / 60;
        return h > 0 ? Loc.F("{0}h {1:00}m", h, m) : Loc.F("{0}m", m);
    }

    /// <summary>True when the culture's short time pattern uses a 24 hour clock.</summary>
    public static bool Uses24Hours => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');

    /// <summary>The label of an hour of the day ("6" or "6 AM").</summary>
    public static string HourLabel(int hour) => DateTime.Today.AddHours(hour).ToString(Uses24Hours ? "H" : "h tt", CultureInfo.CurrentCulture);

    /// <summary>The tooltip range of an hour column.</summary>
    public static string HourRange(int hour)
    {
        var pattern = Uses24Hours ? "H:00" : "h tt";
        return DateTime.Today.AddHours(hour).ToString(pattern, CultureInfo.CurrentCulture)
            + "–" + DateTime.Today.AddHours(hour + 1).ToString(pattern, CultureInfo.CurrentCulture);
    }

    /// <summary>The first-use time of a day in the culture's short time ("08:41" stored, shown as the culture writes it).</summary>
    public static string FirstUse(string? hhmm) =>
        DateTime.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("t", CultureInfo.CurrentCulture)
            : hhmm ?? "";
}
