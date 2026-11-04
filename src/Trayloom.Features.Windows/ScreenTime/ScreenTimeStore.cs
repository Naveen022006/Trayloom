// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>
/// Day files of Screen time (spec 06 section 1.4): <c>days\yyyy-MM-dd.json</c> under the feature's data folder, written atomically so a
/// crash never leaves half a file. Only dates, app paths, names and seconds are stored; window titles are never read.
/// </summary>
internal sealed class ScreenTimeStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly string _root;
    private readonly string _days;
    private readonly ILog _log;
    private bool _writeFailureLogged;

    public ScreenTimeStore(string root, ILog log)
    {
        _root = root;
        _days = Path.Combine(root, "days");
        _log = log;
    }

    /// <summary>The feature's data folder.</summary>
    public string Root => _root;

    private string PathOf(DateTime date, bool bad = false) => Path.Combine(_days, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (bad ? ".bad.json" : ".json"));

    /// <summary>Loads a day; null when there is no file. A file that does not parse is renamed to <c>.bad.json</c> and counts as no file.</summary>
    public DayUsage? Load(DateTime date)
    {
        var path = PathOf(date);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<DayFile>(File.ReadAllText(path), Options);
            if (file is null || file.Hours.Length != 24)
            {
                throw new JsonException("shape");
            }

            var day = new DayUsage(date) { First = file.First };
            for (var h = 0; h < 24; h++)
            {
                day.Hours[h] = Math.Max(0, file.Hours[h]);
            }

            foreach (var app in file.Apps)
            {
                if (!string.IsNullOrEmpty(app.Id) && app.Seconds > 0)
                {
                    day.Apps[app.Id] = new AppUsage { Id = app.Id, Name = app.Name, Seconds = app.Seconds };
                }
            }

            return day;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn("[screen-time] day file unreadable, kept as .bad");
            try
            {
                File.Move(path, PathOf(date, bad: true), overwrite: true);
            }
            catch (Exception)
            {
                // the file stays where it is; the day starts from zero in memory and the next write replaces it
            }

            return null;
        }
    }

    /// <summary>Writes a day atomically. Returns false when the folder is not writable (logged once per run).</summary>
    public bool Save(DayUsage day, DayFile? shape = null)
    {
        try
        {
            Directory.CreateDirectory(_days);
            var file = shape ?? new DayFile();
            file.Date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            file.First = day.First;
            file.Total = (int)Math.Round(day.Total);
            for (var h = 0; h < 24; h++)
            {
                file.Hours[h] = (int)Math.Round(day.Hours[h]);
            }

            file.Apps = [.. day.Ranked().Select(a => new AppFile { Id = a.Id, Name = a.Name, Seconds = (int)Math.Round(a.Seconds) })];
            var path = PathOf(day.Date);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
            File.Move(temp, path, overwrite: true);
            _writeFailureLogged = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_writeFailureLogged)
            {
                _writeFailureLogged = true;
                _log.Warn($"[screen-time] cannot write day file: {ex.HResult}");
            }

            return false;
        }
    }

    /// <summary>Deletes day files that are <paramref name="keepDays"/> days or more behind <paramref name="today"/> (today counts as day 1).</summary>
    public void Sweep(int keepDays, DateTime today)
    {
        if (!Directory.Exists(_days))
        {
            return;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_days, "*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.Length >= 10
                && DateTime.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && (today.Date - date.Date).TotalDays >= keepDays)
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // retried at the next sweep
                }
            }
        }

        if (removed > 0)
        {
            _log.Info($"[screen-time] retention: removed {removed} old day files");
        }
    }

    /// <summary>Deletes every day file ("Clear history…").</summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(_days))
            {
                Directory.Delete(_days, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"[screen-time] cannot clear history: {ex.HResult}");
        }
    }

    /// <summary>Deletes the whole data folder (the feature was uninstalled).</summary>
    public void DeleteAll()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"[screen-time] cannot delete data folder: {ex.HResult}");
        }
    }
}
