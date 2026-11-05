// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>One scratchpad tab.</summary>
internal sealed class PadItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("D").ToUpperInvariant();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Seconds since 2001-01-01 UTC (Swift's default date encoding); omitted when never edited.</summary>
    [JsonPropertyName("modifiedAt")]
    [JsonConverter(typeof(PadDateConverter))]
    public double? ModifiedAt { get; set; }
}

/// <summary>The persisted document: <c>{"pads":[...],"selectedID":"..."}</c>.</summary>
internal sealed class PadDocument
{
    [JsonPropertyName("pads")]
    public List<PadItem> Pads { get; set; } = [];

    [JsonPropertyName("selectedID")]
    public string SelectedId { get; set; } = string.Empty;
}

/// <summary>Reads a number (reference date seconds) or an ISO-8601 string.</summary>
internal sealed class PadDateConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String when DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date):
                return PadRules.ToReference(date.UtcDateTime);
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is { } v)
        {
            writer.WriteNumberValue(v);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>Pure rules of the scratchpad document (sanitising, naming, retention).</summary>
internal static class PadRules
{
    public const int MaxPads = 12;
    public const int MaxNameLength = 40;
    public const string DefaultName = "Scratchpad";

    private static readonly DateTime Reference = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static double ToReference(DateTime utc) => (utc - Reference).TotalSeconds;

    public static double Now() => ToReference(DateTime.UtcNow);

    /// <summary>Local time of a stored reference-date timestamp.</summary>
    public static DateTime FromReference(double seconds) => Reference.AddSeconds(seconds).ToLocalTime();

    /// <summary>Collapses whitespace and cuts to 40 characters.</summary>
    public static string CleanName(string name)
    {
        var collapsed = Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
        return collapsed.Length > MaxNameLength ? collapsed[..MaxNameLength].TrimEnd() : collapsed;
    }

    /// <summary><c>nextPadName</c>: "Scratchpad 1" first, then the lowest unused "Scratchpad n" up to 12, else count + 1.</summary>
    public static string NextName(string defaultName, IReadOnlyCollection<string> existing)
    {
        var baseName = CleanName(defaultName);
        if (baseName.Length == 0)
        {
            baseName = DefaultName;
        }

        bool Used(string n) => existing.Any(e => string.Equals(e, n, StringComparison.Ordinal));
        if (!Used(baseName) && !Used(baseName + " 1"))
        {
            return baseName + " 1";
        }

        for (var n = 2; n <= MaxPads; n++)
        {
            if (!Used($"{baseName} {n}"))
            {
                return $"{baseName} {n}";
            }
        }

        return $"{baseName} {existing.Count + 1}";
    }

    /// <summary><c>ScratchpadDocument.sanitized</c>.</summary>
    public static PadDocument Sanitize(PadDocument? doc)
    {
        var result = new PadDocument();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pad in doc?.Pads ?? [])
        {
            if (result.Pads.Count >= MaxPads)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(pad.Id) || !seen.Add(pad.Id))
            {
                continue;
            }

            var name = CleanName(pad.Name);
            if (name.Length == 0)
            {
                name = NextName(DefaultName, result.Pads.Select(p => p.Name).ToList());
            }

            var text = pad.Text ?? string.Empty;
            result.Pads.Add(new PadItem { Id = pad.Id, Name = name, Text = text, ModifiedAt = text.Length == 0 ? null : pad.ModifiedAt });
        }

        if (result.Pads.Count == 0)
        {
            result.Pads.Add(new PadItem { Name = DefaultName + " 1" });
        }

        result.SelectedId = result.Pads.Any(p => p.Id == doc?.SelectedId) ? doc!.SelectedId : result.Pads[0].Id;
        return result;
    }

    /// <summary>Retention: idle longer than the limit empties the pad's text (evaluated only at open time).</summary>
    public static int ApplyRetention(PadDocument doc, string retention, double now)
    {
        var limit = retention switch
        {
            "day" => 86400.0,
            "week" => 7 * 86400.0,
            "month" => 30 * 86400.0,
            _ => 0.0,
        };
        if (limit <= 0)
        {
            return 0;
        }

        var cleared = 0;
        foreach (var pad in doc.Pads)
        {
            if (pad.ModifiedAt is { } modified && pad.Text.Length > 0 && now - modified > limit)
            {
                pad.Text = string.Empty;
                pad.ModifiedAt = null;
                cleared++;
            }
        }

        return cleared;
    }
}

/// <summary>
/// The scratchpad document on disk (<c>Scratchpad.json</c>): load on open, 0.8 s debounced autosave, atomic write with
/// read-back verification, retention, and migration of the old <c>notes.json</c>.
/// </summary>
internal sealed class PadStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _file;
    private readonly string _legacyNotes;
    private readonly DispatcherTimer _timer;
    private readonly Func<string> _retention;
    private bool _dirty;
    private bool _canSave;

    public PadStore(Dispatcher ui, string file, string legacyNotes, Func<string> retention)
    {
        _file = file;
        _legacyNotes = legacyNotes;
        _retention = retention;
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, _) => Flush();
    }

    /// <summary>The in-memory document (null until loaded).</summary>
    public PadDocument? Doc { get; private set; }

    /// <summary>True while the last write failed (the pad shows the banner).</summary>
    public bool SaveFailed { get; private set; }

    /// <summary>Raised when <see cref="SaveFailed"/> changed.</summary>
    public event Action? SaveStateChanged;

    public PadItem Selected => Doc!.Pads.First(p => p.Id == Doc.SelectedId);

    /// <summary>Raised after the document changed through <see cref="Replace"/> / <see cref="Touch"/> / <see cref="SaveNow"/>.</summary>
    public event Action? DocumentChanged;

    /// <summary>Replaces the pads from outside (Dynamic Island): ids are kept, 1 to 12 pads, saved immediately.</summary>
    public void Replace(IReadOnlyList<(string Id, string Name, string Text)> pads, string? selectedId)
    {
        if (Doc is null || pads.Count == 0)
        {
            return;
        }

        var existing = Doc.Pads.ToDictionary(p => p.Id);
        var list = new List<PadItem>();
        foreach (var (id, name, text) in pads.Take(PadRules.MaxPads))
        {
            var clean = PadRules.CleanName(name);
            var normalized = text.Replace("\r\n", "\n");
            if (existing.TryGetValue(id, out var pad))
            {
                if (pad.Text != normalized)
                {
                    pad.Text = normalized;
                    pad.ModifiedAt = normalized.Length == 0 ? null : PadRules.Now();
                }

                pad.Name = clean.Length == 0 ? pad.Name : clean;
            }
            else
            {
                pad = new PadItem { Id = id, Name = clean.Length == 0 ? PadRules.NextName(PadRules.DefaultName, list.Select(p => p.Name).ToList()) : clean, Text = normalized, ModifiedAt = normalized.Length == 0 ? null : PadRules.Now() };
            }

            list.Add(pad);
        }

        Doc.Pads = list;
        Doc.SelectedId = list.Any(p => p.Id == selectedId) ? selectedId! : list[0].Id;
        SaveNow();
    }

    /// <summary>Loads (or reuses) the document; false when the file could not be read (nothing is saved afterwards).</summary>
    public bool Load()
    {
        if (Doc is not null && _dirty)
        {
            Flush();
            return true;
        }

        try
        {
            PadDocument? doc = null;
            var migrated = false;
            if (File.Exists(_file))
            {
                doc = JsonSerializer.Deserialize<PadDocument>(File.ReadAllText(_file, Encoding.UTF8), Json);
            }
            else if (File.Exists(_legacyNotes))
            {
                doc = MigrateNotes(_legacyNotes);
                migrated = true;
            }

            Doc = PadRules.Sanitize(doc);
            foreach (var pad in Doc.Pads)
            {
                pad.Text = pad.Text.Replace("\r\n", "\n");
            }

            _canSave = true;
            try
            {
                // A crash between writing and renaming leaves a stale temp copy of the pads behind.
                File.Delete(_file + ".tmp");
            }
            catch
            {
                // best effort
            }

            var cleared = PadRules.ApplyRetention(Doc, _retention(), PadRules.Now());
            if (migrated || cleared > 0)
            {
                Save();
                if (migrated)
                {
                    try
                    {
                        File.Move(_legacyNotes, _legacyNotes + ".migrated", overwrite: true);
                    }
                    catch
                    {
                        // the archive is best effort
                    }
                }
            }

            return true;
        }
        catch
        {
            _canSave = false;
            Doc = null;
            return false;
        }
    }

    private static PadDocument MigrateNotes(string path)
    {
        var doc = new PadDocument();
        var time = File.GetLastWriteTimeUtc(path);
        using var parsed = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var element in parsed.RootElement.EnumerateArray())
        {
            if (doc.Pads.Count >= PadRules.MaxPads)
            {
                break;
            }

            var text = element.TryGetProperty("Text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            doc.Pads.Add(new PadItem
            {
                Name = PadRules.NextName(PadRules.DefaultName, doc.Pads.Select(p => p.Name).ToList()),
                Text = text.Replace("\r\n", "\n"),
                ModifiedAt = text.Length == 0 ? null : PadRules.ToReference(time),
            });
        }

        return doc;
    }

    /// <summary>Marks the document changed and schedules the debounced save.</summary>
    public void Touch()
    {
        _dirty = true;
        _timer.Stop();
        _timer.Start();
        DocumentChanged?.Invoke();
    }

    /// <summary>Updates the selected pad's text (only when it changed).</summary>
    public void SetText(string text)
    {
        var pad = Selected;
        var normalized = text.Replace("\r\n", "\n");
        if (pad.Text == normalized)
        {
            return;
        }

        pad.Text = normalized;
        pad.ModifiedAt = normalized.Length == 0 ? null : PadRules.Now();
        Touch();
    }

    public PadItem? Add()
    {
        if (Doc is null || Doc.Pads.Count >= PadRules.MaxPads)
        {
            return null;
        }

        var pad = new PadItem { Name = PadRules.NextName(PadRules.DefaultName, Doc.Pads.Select(p => p.Name).ToList()) };
        Doc.Pads.Add(pad);
        Doc.SelectedId = pad.Id;
        SaveNow();
        return pad;
    }

    public void Select(string id)
    {
        if (Doc is not null && Doc.Pads.Any(p => p.Id == id) && Doc.SelectedId != id)
        {
            Doc.SelectedId = id;
            SaveNow();
        }
    }

    public bool Rename(string id, string name)
    {
        var clean = PadRules.CleanName(name);
        var pad = Doc?.Pads.FirstOrDefault(p => p.Id == id);
        if (pad is null || clean.Length == 0)
        {
            return false;
        }

        pad.Name = clean;
        SaveNow();
        return true;
    }

    public bool Remove(string id)
    {
        if (Doc is null || Doc.Pads.Count <= 1)
        {
            return false;
        }

        var index = Doc.Pads.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return false;
        }

        var wasSelected = Doc.SelectedId == id;
        Doc.Pads.RemoveAt(index);
        if (wasSelected)
        {
            Doc.SelectedId = Doc.Pads[Math.Min(index, Doc.Pads.Count - 1)].Id;
        }

        SaveNow();
        return true;
    }

    public void SaveNow()
    {
        _dirty = true;
        Flush();
        DocumentChanged?.Invoke();
    }

    /// <summary>Writes the document when it changed.</summary>
    public void Flush()
    {
        _timer.Stop();
        if (_dirty)
        {
            Save();
        }
    }

    /// <summary>Drops the in-memory copy (after a settings import) so a later flush cannot overwrite the file.</summary>
    public void Forget()
    {
        _timer.Stop();
        _dirty = false;
        Doc = null;
    }

    private bool Save()
    {
        if (Doc is null || !_canSave)
        {
            return false;
        }

        var ok = false;
        try
        {
            var export = new PadDocument { SelectedId = Doc.SelectedId };
            foreach (var pad in Doc.Pads)
            {
                export.Pads.Add(new PadItem { Id = pad.Id, Name = pad.Name, Text = pad.Text, ModifiedAt = pad.ModifiedAt });
            }

            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(export, Json));
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(_file))
            {
                File.Replace(temp, _file, null);
            }
            else
            {
                File.Move(temp, _file);
            }

            ok = File.ReadAllBytes(_file).AsSpan().SequenceEqual(data);
        }
        catch
        {
            ok = false;
        }

        _dirty = !ok;
        if (SaveFailed == ok)
        {
            SaveFailed = !ok;
            SaveStateChanged?.Invoke();
        }

        return ok;
    }
}
