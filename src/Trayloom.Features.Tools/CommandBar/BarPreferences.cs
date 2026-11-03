// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>The persisted preferences of the Command Bar (spec 1.13) over the flat settings store.</summary>
internal sealed class BarPreferences
{
    public const int MaxPins = 30;
    public const int MaxLinks = 60;
    public const int MaxRowShortcuts = 64;

    private readonly IAppSettings _app;

    public BarPreferences(IAppSettings app)
    {
        _app = app;
        ToolsDefaults.Register(app);
        app.Register("commandBarUsage", "");
        app.Register("commandBarQueryHabits", "");
        app.Register("commandBarQueryHabitKey", "");
        app.Register("killProcessCommandBarEnabled", true);
        app.Register("uninstallerCommandBarEnabled", false);
    }

    public IAppSettings App => _app;

    public bool CompactMode => _app.Get<bool>("commandBarCompactMode");

    public bool AsciiLayout => _app.Get<bool>("commandBarASCIILayoutEnabled");

    public string SkinTone => _app.Get<string>("commandBarEmojiSkinTone") ?? "";

    // ---- sources ----

    public HashSet<string> DisabledSources() =>
        [.. (_app.Get<string>("commandBarDisabledSources") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => s != "actions")];

    public bool IsSourceEnabled(string raw) => raw == "actions" || !DisabledSources().Contains(raw);

    public void SetSourceEnabled(string raw, bool enabled)
    {
        if (raw == "actions")
        {
            return;
        }

        var set = DisabledSources();
        if (enabled)
        {
            set.Remove(raw);
        }
        else
        {
            set.Add(raw);
        }

        _app.Set("commandBarDisabledSources", string.Join(',', set.OrderBy(s => s, StringComparer.Ordinal)));
    }

    // ---- aliases ----

    public Dictionary<string, string> Aliases()
    {
        try
        {
            var json = _app.Get<string>("commandBarAliases");
            var dict = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            return dict.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToDictionary(p => p.Key, p => p.Value.Trim());
        }
        catch
        {
            return [];
        }
    }

    public string? AliasOf(string key) => Aliases().GetValueOrDefault(key);

    public void SetAlias(string key, string? alias)
    {
        var dict = Aliases();
        var clean = (alias ?? "").Trim();
        if (clean.Length > 60)
        {
            clean = clean[..60];
        }

        if (clean.Length == 0)
        {
            dict.Remove(key);
        }
        else
        {
            dict[key] = clean;
        }

        _app.Set("commandBarAliases", dict.Count == 0 ? "" : JsonSerializer.Serialize(dict));
    }

    /// <summary>Another key already answering to one of the alias words (null when free).</summary>
    public string? AliasOwner(string key, string alias)
    {
        var words = BarSearch.Normalize(alias).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach (var (other, value) in Aliases())
        {
            if (other == key)
            {
                continue;
            }

            if (BarSearch.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(words.Contains))
            {
                return other;
            }
        }

        return null;
    }

    // ---- pins / hidden ----

    public List<string> Pins() => [.. (_app.Get<string>("commandBarPins") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];

    public bool IsPinned(string key) => Pins().Contains(key);

    public void SetPinned(string key, bool pinned)
    {
        var pins = Pins();
        pins.Remove(key);
        if (pinned)
        {
            pins.Add(key);
            while (pins.Count > MaxPins)
            {
                pins.RemoveAt(0);
            }
        }

        _app.Set("commandBarPins", string.Join('\n', pins));
    }

    public HashSet<string> Hidden() => [.. (_app.Get<string>("commandBarHidden") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void SetHidden(string key, bool hidden)
    {
        var set = Hidden();
        if (hidden)
        {
            set.Add(key);
        }
        else
        {
            set.Remove(key);
        }

        _app.Set("commandBarHidden", string.Join('\n', set.OrderBy(s => s, StringComparer.Ordinal)));
    }

    // ---- links ----

    public List<CommandBarLink> Links()
    {
        try
        {
            var json = _app.Get<string>("commandBarLinks");
            var list = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<CommandBarLink>>(json, LinkOptions) ?? [];
            return [.. list.Where(l => !string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(l.Destination)).Take(MaxLinks)];
        }
        catch
        {
            return [];
        }
    }

    public void SaveLinks(IEnumerable<CommandBarLink> links) =>
        _app.Set("commandBarLinks", JsonSerializer.Serialize(links.Take(MaxLinks).ToList(), LinkOptions));

    private static readonly JsonSerializerOptions LinkOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    // ---- row shortcuts ----

    public Dictionary<string, Hotkey> RowShortcuts()
    {
        try
        {
            var json = _app.Get<string>("commandBarRowShortcuts");
            var raw = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            var result = new Dictionary<string, Hotkey>();
            foreach (var (key, text) in raw)
            {
                if (Hotkey.TryParse(text, out var hk))
                {
                    result[key] = hk;
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    public void SetRowShortcut(string key, Hotkey? hotkey)
    {
        var dict = RowShortcuts();
        if (hotkey is null)
        {
            dict.Remove(key);
        }
        else
        {
            foreach (var other in dict.Where(p => p.Value.Equals(hotkey) && p.Key != key).Select(p => p.Key).ToList())
            {
                dict.Remove(other);
            }

            dict[key] = hotkey;
        }

        _app.Set("commandBarRowShortcuts", dict.Count == 0 ? "" : JsonSerializer.Serialize(dict.ToDictionary(p => p.Key, p => p.Value.ToString())));
    }

    // ---- position ----

    public (int Dx, int Dy)? PositionOffset
    {
        get
        {
            var text = _app.Get<string>("commandBarPositionOffset") ?? "";
            var parts = text.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0], out var dx) && int.TryParse(parts[1], out var dy) && (dx != 0 || dy != 0) ? (dx, dy) : null;
        }
        set => _app.Set("commandBarPositionOffset", value is { } v && (v.Dx != 0 || v.Dy != 0) ? $"{v.Dx},{v.Dy}" : "");
    }

    public bool HasCustomPosition => PositionOffset is not null;

    // ---- files ----

    public List<string> FileScopes() => [.. (_app.Get<string>("commandBarFileScopes") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void SetFileScopes(IEnumerable<string> scopes) => _app.Set("commandBarFileScopes", string.Join('\n', scopes));

    public List<string> FileIgnores() => [.. (_app.Get<string>("commandBarFileIgnores") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void SetFileIgnores(IEnumerable<string> ignores) => _app.Set("commandBarFileIgnores", string.Join('\n', ignores));
}

/// <summary>Usage counts, hashed query habits and the session memory (spec 1.6.4).</summary>
internal sealed class BarLearning
{
    private sealed class UsageEntry
    {
        public int Count { get; set; }

        public double LastUsed { get; set; }
    }

    private readonly IAppSettings _app;
    private Dictionary<string, UsageEntry>? _usage;
    private Dictionary<string, Dictionary<string, UsageEntry>>? _habits;
    private byte[]? _key;
    private readonly Dictionary<string, List<(string Id, int Count)>> _memory = [];

    public BarLearning(IAppSettings app) => _app = app;

    private static double Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private Dictionary<string, UsageEntry> Usage => _usage ??= Load<Dictionary<string, UsageEntry>>("commandBarUsage") ?? [];

    private Dictionary<string, Dictionary<string, UsageEntry>> Habits => _habits ??= Load<Dictionary<string, Dictionary<string, UsageEntry>>>("commandBarQueryHabits") ?? [];

    private T? Load<T>(string key)
    {
        try
        {
            var json = _app.Get<string>(key);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return default;
        }
    }

    private static int UsageBoost(UsageEntry e)
    {
        var age = Now - e.LastUsed;
        var weight = age < 2 * 3600 ? 12 : age < 48 * 3600 ? 8 : age < 14 * 86400 ? 5 : 2;
        return Math.Min(e.Count, 40) * weight;
    }

    public int UsageBoostOf(string rowId) => Usage.TryGetValue(rowId, out var e) ? UsageBoost(e) : 0;

    public int UseCount(string rowId) => Usage.TryGetValue(rowId, out var e) ? e.Count : 0;

    public double LastUsed(string rowId) => Usage.TryGetValue(rowId, out var e) ? e.LastUsed : 0;

    private byte[]? HabitKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        var text = _app.Get<string>("commandBarQueryHabitKey");
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = Convert.FromBase64String(text);
                if (bytes.Length == 32)
                {
                    return _key = bytes;
                }
            }
        }
        catch
        {
            // damaged key: habits are discarded below
        }

        _key = RandomNumberGenerator.GetBytes(32);
        _app.Set("commandBarQueryHabitKey", Convert.ToBase64String(_key));
        _habits = [];
        _app.Set("commandBarQueryHabits", "");
        return _key;
    }

    private string Digest(string prefix)
    {
        using var hmac = new HMACSHA256(HabitKey()!);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(prefix)))[..24].ToLowerInvariant();
    }

    /// <summary>Records a run of a row for a typed (normalised) query.</summary>
    public void Record(string rowId, bool countsUsage, string normQuery, bool emojiQuery = false)
    {
        if (!countsUsage)
        {
            return;
        }

        var usage = Usage;
        if (!usage.TryGetValue(rowId, out var e))
        {
            usage[rowId] = e = new UsageEntry();
        }

        e.Count = Math.Min(e.Count + 1, 999);
        e.LastUsed = Now;
        while (usage.Count > 200)
        {
            usage.Remove(usage.OrderBy(p => p.Value.LastUsed).First().Key);
        }

        _app.Set("commandBarUsage", JsonSerializer.Serialize(usage));

        var q = normQuery.Length > 24 ? normQuery[..24] : normQuery;
        if (q.Length > 0)
        {
            var habits = Habits;
            for (var len = 1; len <= q.Length; len++)
            {
                var digest = Digest(q[..len]);
                if (!habits.TryGetValue(digest, out var rows))
                {
                    habits[digest] = rows = [];
                }

                if (!rows.TryGetValue(rowId, out var h))
                {
                    rows[rowId] = h = new UsageEntry();
                }

                h.Count = Math.Min(h.Count + 1, 999);
                h.LastUsed = Now;
                while (rows.Count > 4)
                {
                    rows.Remove(rows.OrderBy(p => p.Value.Count).ThenBy(p => p.Value.LastUsed).First().Key);
                }
            }

            while (habits.Count > 320)
            {
                habits.Remove(habits.OrderBy(p => p.Value.Values.Max(v => v.LastUsed)).First().Key);
            }

            _app.Set("commandBarQueryHabits", JsonSerializer.Serialize(habits));
            RememberMemory(q, rowId);
        }
    }

    /// <summary>Habit priority per row id for a typed query.</summary>
    public Dictionary<string, int> HabitPriorities(string normQuery)
    {
        var result = new Dictionary<string, int>();
        var q = normQuery.Length > 24 ? normQuery[..24] : normQuery;
        if (q.Length == 0 || Habits.Count == 0)
        {
            return result;
        }

        for (var len = 1; len <= q.Length; len++)
        {
            if (!Habits.TryGetValue(Digest(q[..len]), out var rows))
            {
                continue;
            }

            foreach (var (id, h) in rows)
            {
                var boost = Math.Min(UsageBoost(h) * 5 + len * 6, 720);
                if (!result.TryGetValue(id, out var old) || boost > old)
                {
                    result[id] = boost;
                }
            }
        }

        return result;
    }

    private void RememberMemory(string q, string id)
    {
        var max = Math.Min(q.Length, 12);
        for (var len = 1; len <= max; len++)
        {
            var key = q[..len];
            if (!_memory.TryGetValue(key, out var list))
            {
                if (_memory.Count >= 60)
                {
                    _memory.Remove(_memory.Keys.First());
                }

                _memory[key] = list = [];
            }

            var i = list.FindIndex(x => x.Id == id);
            if (i >= 0)
            {
                list[i] = (id, Math.Min(list[i].Count + 1, 99));
            }
            else
            {
                list.Add((id, 1));
                if (list.Count > 4)
                {
                    list.RemoveAt(0);
                }
            }
        }
    }

    public int MemoryBoost(string normQuery, string rowId)
    {
        var key = normQuery.Length > 12 ? normQuery[..12] : normQuery;
        return _memory.TryGetValue(key, out var list) && list.FirstOrDefault(x => x.Id == rowId) is { Id: not null } hit ? Math.Min(hit.Count, 3) : 0;
    }

    public void Forget(string rowId)
    {
        Usage.Remove(rowId);
        _app.Set("commandBarUsage", JsonSerializer.Serialize(Usage));
        foreach (var rows in Habits.Values)
        {
            rows.Remove(rowId);
        }

        _app.Set("commandBarQueryHabits", JsonSerializer.Serialize(Habits));
        foreach (var list in _memory.Values)
        {
            list.RemoveAll(x => x.Id == rowId);
        }
    }

    public void ForgetAll()
    {
        _usage = [];
        _habits = [];
        _memory.Clear();
        _app.Set("commandBarUsage", "");
        _app.Set("commandBarQueryHabits", "");
    }

    /// <summary>Most used rows first (count desc, last used desc).</summary>
    public IEnumerable<string> MostUsed() => Usage.OrderByDescending(p => p.Value.Count).ThenByDescending(p => p.Value.LastUsed).Select(p => p.Key);
}
