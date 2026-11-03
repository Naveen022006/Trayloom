// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>US dollars per million tokens.</summary>
public sealed record AgentPrice(double Input, double Output, double CacheRead, double CacheWrite, double CacheWriteLong, double FastMultiplier = 1, AgentLongContext? LongContext = null);

/// <summary>Past <c>Above</c> prompt tokens, input and cache rates are multiplied by <c>Input</c> and output by <c>Output</c>.</summary>
public sealed record AgentLongContext(long Above, double Input, double Output);

/// <summary>Every price the island knows (<c>agent-prices.json</c>, schema 1).</summary>
public sealed class AgentPriceList
{
    public sealed record Model(string Id, AgentPrice Price);

    public sealed record Plan(string Match, string Name, double? Monthly);

    public const int Schema = 1;
    public const int MaximumSize = 256 << 10;

    public static readonly AgentPriceList Empty = new() { Updated = DateTime.MinValue };

    public DateTime Updated { get; init; }

    public List<Model> Claude { get; init; } = [];

    public List<Model> Codex { get; init; } = [];

    public List<Plan> ClaudePlans { get; init; } = [];

    public List<Plan> CodexPlans { get; init; } = [];

    public double WebSearch { get; init; }

    public double UsOnlyMultiplier { get; init; } = 1;

    /// <summary>The list shipped inside the assembly.</summary>
    public static AgentPriceList? Bundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trayloom.Agents.agent-prices.json");
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>The newer of two lists; on the same day the downloaded one wins.</summary>
    public static AgentPriceList? Newer(AgentPriceList? shipped, AgentPriceList? downloaded)
    {
        if (downloaded is null)
        {
            return shipped;
        }

        if (shipped is null)
        {
            return downloaded;
        }

        return downloaded.Updated >= shipped.Updated ? downloaded : shipped;
    }

    public static AgentPriceList? Decode(byte[] data)
    {
        if (data.Length > MaximumSize)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Number(root, "schema", 1, 1000) != Schema)
            {
                return null;
            }

            if (!root.TryGetProperty("updated", out var u) || u.ValueKind != JsonValueKind.String || Day(u.GetString()!) is not { } updated)
            {
                return null;
            }

            if (!root.TryGetProperty("claude", out var claude) || claude.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("codex", out var codex) || codex.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var cm = Models(claude, "claude-");
            var xm = Models(codex, null);
            var cp = Plans(claude);
            var xp = Plans(codex);
            if (cm is null || xm is null || cp is null || xp is null
                || Number(claude, "webSearch", 0, 1) is not { } web || Number(claude, "usOnlyMultiplier", 1, 3) is not { } us)
            {
                return null;
            }

            return new AgentPriceList { Updated = updated, Claude = cm, Codex = xm, ClaudePlans = cp, CodexPlans = xp, WebSearch = web, UsOnlyMultiplier = us };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? Number(JsonElement obj, string name, double min, double max)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d))
        {
            return null;
        }

        return double.IsFinite(d) && d >= min && d <= max ? d : null;
    }

    private static List<Model>? Models(JsonElement section, string? prefix)
    {
        if (!section.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var count = arr.GetArrayLength();
        if (count is < 1 or > 500)
        {
            return null;
        }

        var result = new List<Model>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var id = idEl.GetString()!;
            if (!Identifier(id, 64) || !seen.Add(id) || (prefix is not null ? !id.StartsWith(prefix, StringComparison.Ordinal) : id.StartsWith("claude-", StringComparison.Ordinal)))
            {
                return null;
            }

            if (Number(e, "input", 0, 1000) is not { } input || Number(e, "output", 0, 1000) is not { } output || Number(e, "cacheRead", 0, 1000) is not { } cacheRead)
            {
                return null;
            }

            double? cacheWrite = e.TryGetProperty("cacheWrite", out _) ? Number(e, "cacheWrite", 0, 1000) : input;
            double? cacheWriteLong = e.TryGetProperty("cacheWriteLong", out _) ? Number(e, "cacheWriteLong", 0, 1000) : cacheWrite;
            double? fast = e.TryGetProperty("fastMultiplier", out _) ? Number(e, "fastMultiplier", 1, 10) : 1;
            if (cacheWrite is null || cacheWriteLong is null || fast is null)
            {
                return null;
            }

            AgentLongContext? longContext = null;
            if (e.TryGetProperty("longContext", out var lc))
            {
                if (lc.ValueKind != JsonValueKind.Object || Number(lc, "above", 1000, 10_000_000) is not { } above
                    || Number(lc, "inputMultiplier", 1, 10) is not { } li || Number(lc, "outputMultiplier", 1, 10) is not { } lo)
                {
                    return null;
                }

                longContext = new AgentLongContext((long)above, li, lo);
            }

            result.Add(new Model(id, new AgentPrice(input, output, cacheRead, cacheWrite.Value, cacheWriteLong.Value, fast.Value, longContext)));
        }

        return result;
    }

    private static List<Plan>? Plans(JsonElement section)
    {
        if (!section.TryGetProperty("plans", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() > 50)
        {
            return null;
        }

        var result = new List<Plan>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("match", out var m) || m.ValueKind != JsonValueKind.String
                || !Identifier(m.GetString()!, 40) || !e.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var name = n.GetString()!.Trim();
            if (name.Length is < 1 or > 24)
            {
                return null;
            }

            double? monthly = null;
            if (e.TryGetProperty("monthly", out _))
            {
                monthly = Number(e, "monthly", 0, 100_000);
                if (monthly is null)
                {
                    return null;
                }
            }

            result.Add(new Plan(m.GetString()!, name, monthly));
        }

        return result;
    }

    private static bool Identifier(string text, int length)
    {
        if (text.Length < 1 || text.Length > length)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c is '.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"2026-09-22", read as that day in UTC.</summary>
    private static DateTime? Day(string text)
    {
        var parts = text.Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var mo) || !int.TryParse(parts[2], out var d))
        {
            return null;
        }

        if (y is < 2024 or > 2100 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo))
        {
            return null;
        }

        return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
    }
}

/// <summary>Costs are what the same traffic would bill through the API.</summary>
public static class AgentPricing
{
    private static readonly object Gate = new();
    private static AgentPriceList _installed = AgentPriceList.Empty;

    /// <summary>The list prices come from, safe to read from any thread.</summary>
    public static AgentPriceList List
    {
        get
        {
            lock (Gate)
            {
                return _installed;
            }
        }
    }

    /// <summary>Makes <paramref name="list"/> the one prices come from; false when nothing changes.</summary>
    public static bool Install(AgentPriceList list)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_installed, list) || (_installed.Updated == list.Updated && _installed.Claude.Count == list.Claude.Count && _installed.Codex.Count == list.Codex.Count))
            {
                return false;
            }

            _installed = list;
            return true;
        }
    }

    /// <summary>Provider prefixes, snapshot dates and context tags vary by where the model runs; the family name does not.</summary>
    public static string Normalized(string model)
    {
        var id = model.ToLowerInvariant().Trim();
        var at = id.IndexOf("claude-", StringComparison.Ordinal);
        if (at >= 0)
        {
            id = id[at..];
        }

        if (id.StartsWith("claude-", StringComparison.Ordinal))
        {
            id = id.Replace('.', '-');
        }

        var slash = id.LastIndexOf('/');
        if (slash >= 0)
        {
            id = id[(slash + 1)..];
        }

        foreach (var marker in new[] { '@', '[' })
        {
            var i = id.IndexOf(marker);
            if (i >= 0)
            {
                id = id[..i];
            }
        }

        return id;
    }

    private static readonly string[] Siblings = ["mini", "nano", "pro", "lite", "research", "search", "audio", "realtime", "transcribe", "tts", "cyber", "image"];

    /// <summary>The longest matching family wins.</summary>
    public static AgentPrice? PriceFor(string model, AgentPriceList? list = null)
    {
        list ??= List;
        var id = Normalized(model);
        var table = id.StartsWith("claude-", StringComparison.Ordinal) ? list.Claude : list.Codex;
        AgentPriceList.Model? best = null;
        foreach (var m in table)
        {
            if (Matches(id, m.Id) && (best is null || m.Id.Length > best.Id.Length))
            {
                best = m;
            }
        }

        if (best is null)
        {
            return null;
        }

        var rest = id[best.Id.Length..];
        foreach (var s in Siblings)
        {
            if (rest.Contains(s, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return best.Price;
    }

    private static bool Matches(string id, string family)
    {
        if (!id.StartsWith(family, StringComparison.Ordinal))
        {
            return false;
        }

        if (id.Length == family.Length)
        {
            return true;
        }

        var next = id[family.Length];
        if (next is not ('-' or '_' or ':'))
        {
            return false;
        }

        var digits = 0;
        for (var i = family.Length + 1; i < id.Length && char.IsDigit(id[i]); i++)
        {
            digits++;
        }

        return digits == 0 || digits >= 4;
    }

    public static (double? Cost, double Savings) Cost(in AgentBillable billable, string model)
    {
        if (billable.IsAggregate && billable.Tokens.Total == 0)
        {
            return (0, 0);
        }

        var list = List;
        if (PriceFor(model, list) is not { } price)
        {
            return (null, 0);
        }

        var t = billable.Tokens;
        var lng = Math.Min(Math.Max(0, billable.LongCacheWrite), t.CacheWrite);
        var multiplier = billable.Fast ? price.FastMultiplier : 1;
        if (billable.Domestic)
        {
            multiplier *= list.UsOnlyMultiplier;
        }

        var inputRate = multiplier;
        var outputRate = multiplier;
        if (!billable.IsAggregate && price.LongContext is { } lc && t.Prompt > lc.Above)
        {
            inputRate *= lc.Input;
            outputRate *= lc.Output;
        }

        var cost = (t.Input * price.Input * inputRate
                    + (t.CacheWrite - lng) * price.CacheWrite * inputRate
                    + lng * price.CacheWriteLong * inputRate
                    + t.CacheRead * price.CacheRead * inputRate
                    + t.Output * price.Output * outputRate) / 1_000_000
                   + Math.Max(0, billable.WebSearches) * list.WebSearch;
        var savings = t.CacheRead * Math.Max(0, price.Input - price.CacheRead) * inputRate / 1_000_000;
        return (cost, savings);
    }

    /// <summary>Readable model names: "claude-opus-5-5" reads "Opus 5.5", "gpt-6-astra" reads "GPT-6 Astra".</summary>
    public static string DisplayName(string model)
    {
        var id = Normalized(model);
        if (id.Length == 0)
        {
            return "";
        }

        if (id.StartsWith("claude-", StringComparison.Ordinal))
        {
            var colon = id.IndexOf(':');
            var family = colon >= 0 ? id[..colon] : id;
            var parts = family[7..].Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
            parts.RemoveAll(p => p.Length >= 6 && p.All(char.IsDigit));
            parts.RemoveAll(p => p.StartsWith('v') && p.Length > 1 && char.IsDigit(p[1]));
            parts.RemoveAll(p => p == "latest");
            var words = parts.Where(p => !p.All(c => char.IsDigit(c) || c == '.')).ToList();
            var versions = parts.Where(p => p.All(c => char.IsDigit(c) || c == '.')).ToList();
            var version = string.Join('.', versions);
            var name = new List<string> { words.Count > 0 ? Cap(words[0]) : "", version };
            name.AddRange(words.Skip(1).Select(Cap));
            return string.Join(' ', name.Where(s => s.Length > 0));
        }

        if (!id.StartsWith("gpt-", StringComparison.Ordinal))
        {
            return id;
        }

        var gp = id[4..].Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        gp.RemoveAll(p => p == "latest");
        gp.RemoveAll(p => p.Length >= 6 && p.All(char.IsDigit));
        if (gp.Count >= 4 && int.TryParse(gp[^3], out var year) && year is >= 2000 and <= 2100
            && gp.TakeLast(2).All(p => p.Length == 2 && p.All(char.IsDigit)))
        {
            gp.RemoveRange(gp.Count - 3, 3);
        }

        if (gp.Count == 0)
        {
            return "GPT";
        }

        var head = char.IsDigit(gp[0][0]) ? "GPT-" + gp[0] : "GPT " + Cap(gp[0]);
        return string.Join(' ', new[] { head }.Concat(gp.Skip(1).Select(Cap)));
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
}

/// <summary>Subscriptions, recognised from what the agents keep on disk.</summary>
public static class AgentPlans
{
    public static AgentPlan? Claude(string? organizationType, string? rateLimitTier, AgentPriceList? list = null)
    {
        list ??= AgentPricing.List;
        var type = (organizationType ?? "").ToLowerInvariant();
        var recorded = (rateLimitTier ?? "").ToLowerInvariant() + " " + type;
        // Ultra decides which windows exist, so it is recognised first, even when the price list in use (an older download) does not know it.
        var ultra = recorded.Contains("ultra", StringComparison.Ordinal);
        var plan = ultra
            ? list.ClaudePlans.FirstOrDefault(p => p.Match == "ultra")
            : list.ClaudePlans.FirstOrDefault(p => recorded.Contains(p.Match, StringComparison.Ordinal));
        if (ultra && plan is null)
        {
            return new AgentPlan("Ultra", null);
        }

        if (plan is not null)
        {
            return new AgentPlan(plan.Name, plan.Monthly);
        }

        var name = type.Replace("claude_", "", StringComparison.Ordinal).Replace('_', ' ').Trim();
        return name.Length == 0 ? null : new AgentPlan(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name), null);
    }

    public static AgentPlan? Codex(string? planType, AgentPriceList? list = null)
    {
        list ??= AgentPricing.List;
        var raw = planType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var plan = list.CodexPlans.FirstOrDefault(p => p.Match == raw);
        if (plan is not null)
        {
            return new AgentPlan(plan.Name, plan.Monthly);
        }

        return new AgentPlan(char.ToUpperInvariant(raw[0]) + raw[1..], null);
    }
}
