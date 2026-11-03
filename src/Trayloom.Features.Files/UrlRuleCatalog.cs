// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Files;

/// <summary>One parameter name inside a cleaning-rule group.</summary>
/// <param name="Name">Lower-case name (<c>utm_*</c> for the prefix rule).</param>
/// <param name="IsUser">Added by the user (can be deleted).</param>
/// <param name="Enabled">Removed from links when on; kept when off.</param>
internal sealed record RuleName(string Name, bool IsUser, bool Enabled);

/// <summary>A site (or "All sites" for the empty key) and its parameter names.</summary>
internal sealed record RuleGroup(string Key, string Title, IReadOnlyList<RuleName> Names)
{
    public int EnabledCount => Names.Count(n => n.Enabled);
}

/// <summary>Rule groups shown by the Clean URL settings page and panel, and the three stored strings behind them.</summary>
internal static class UrlRuleCatalog
{
    private static readonly char[] Forbidden = ['|', ',', '&', '='];

    /// <summary>Lower-cases and validates a parameter name; null when it is empty or contains whitespace or one of <c>| , &amp; =</c>.</summary>
    public static string? NormalizeName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return name.Length == 0 || name.Any(char.IsWhiteSpace) || name.IndexOfAny(Forbidden) >= 0 ? null : name;
    }

    /// <summary>Normalises a site key (also accepts a pasted URL); null when it is not a host with a dot.</summary>
    public static string? NormalizeSiteKey(string? raw)
    {
        var site = (raw ?? string.Empty).Trim().ToLowerInvariant();
        var scheme = site.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            site = site[(scheme + 3)..];
        }

        var cut = site.IndexOfAny(['/', '?', '#']);
        if (cut >= 0)
        {
            site = site[..cut];
        }

        var at = site.LastIndexOf('@');
        if (at >= 0)
        {
            site = site[(at + 1)..];
        }

        var colon = site.LastIndexOf(':');
        if (colon >= 0 && !site.Contains(']') && site[(colon + 1)..].All(char.IsDigit))
        {
            site = site[..colon];
        }

        if (site.StartsWith("www.", StringComparison.Ordinal))
        {
            site = site[4..];
        }

        if (!site.Contains('.') || site.StartsWith('.') || site.EndsWith('.') || site.Any(char.IsWhiteSpace) || site.Contains('|') || site.Contains(','))
        {
            return null;
        }

        return site;
    }

    /// <summary>The built-in global names: <c>utm_*</c> first, then sorted.</summary>
    public static IReadOnlyList<string> GlobalBuiltIns { get; } = [UrlCleaner.UtmWildcard, .. UrlCleaner.TrackedParameters.OrderBy(n => n, StringComparer.Ordinal)];

    public static string Title(string key) => key.Length == 0 ? Strings.CleanUrl.AllSites : key;

    /// <summary>Builds the groups from the current rules: "All sites" first, then sites alphabetically (only groups with entries).</summary>
    public static List<RuleGroup> Groups(UrlRules rules)
    {
        var groups = new List<RuleGroup>();
        var keys = UrlCleaner.HostParameters.Keys.Concat(rules.Added.Keys).Where(k => k.Length > 0).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        groups.Add(Build(string.Empty, rules));
        foreach (var key in keys)
        {
            var group = Build(key, rules);
            if (group.Names.Count > 0)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private static RuleGroup Build(string key, UrlRules rules)
    {
        rules.Disabled.TryGetValue(key, out var off);
        rules.Added.TryGetValue(key, out var added);
        var names = new List<RuleName>();
        IEnumerable<string> builtIn = key.Length == 0 ? GlobalBuiltIns : UrlCleaner.HostParameters.TryGetValue(key, out var list) ? list : [];
        foreach (var name in builtIn)
        {
            names.Add(new RuleName(name, false, !(off?.Contains(name) ?? false)));
        }

        if (added is not null)
        {
            foreach (var name in added.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (names.All(n => n.Name != name))
                {
                    names.Add(new RuleName(name, true, !(off?.Contains(name) ?? false)));
                }
            }
        }

        return new RuleGroup(key, Title(key), names);
    }

    // ---------------------------------------------------------------------------------------------- stored strings

    public static UrlRules Load(IAppSettings app) =>
        UrlRules.Parse(app.Get<string>("urlCleanerCustomParameters"), app.Get<string>("urlCleanerSiteParameters"), app.Get<string>("urlCleanerDisabledParameters"));

    public static void Save(IAppSettings app, UrlRules rules)
    {
        var global = rules.Added.TryGetValue(string.Empty, out var g) ? string.Join(",", g.OrderBy(n => n, StringComparer.Ordinal)) : string.Empty;
        var site = string.Join(",", rules.Added.Where(p => p.Key.Length > 0).OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value.OrderBy(n => n, StringComparer.Ordinal).Select(n => p.Key + "|" + n)));
        var disabled = string.Join(",", rules.Disabled.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value.OrderBy(n => n, StringComparer.Ordinal).Select(n => p.Key + "|" + n)));
        app.Set("urlCleanerCustomParameters", global);
        app.Set("urlCleanerSiteParameters", site);
        app.Set("urlCleanerDisabledParameters", disabled);
    }

    public static void SetEnabled(UrlRules rules, string group, string name, bool enabled)
    {
        if (!rules.Disabled.TryGetValue(group, out var off))
        {
            rules.Disabled[group] = off = new HashSet<string>(StringComparer.Ordinal);
        }

        if (enabled)
        {
            off.Remove(name);
        }
        else
        {
            off.Add(name);
        }

        if (off.Count == 0)
        {
            rules.Disabled.Remove(group);
        }
    }

    /// <summary>Adds a user name to a group (an earlier switch-off is cleared). Returns false when it already exists as a built-in or user name that is on.</summary>
    public static bool AddName(UrlRules rules, string group, string name)
    {
        var builtIn = group.Length == 0 ? GlobalBuiltIns.Contains(name) : UrlCleaner.HostParameters.TryGetValue(group, out var list) && list.Contains(name);
        if (!builtIn)
        {
            if (!rules.Added.TryGetValue(group, out var set))
            {
                rules.Added[group] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(name);
        }

        SetEnabled(rules, group, name, true);
        return true;
    }

    /// <summary>Deletes a user-added name (and its off record).</summary>
    public static void RemoveName(UrlRules rules, string group, string name)
    {
        if (rules.Added.TryGetValue(group, out var set))
        {
            set.Remove(name);
            if (set.Count == 0)
            {
                rules.Added.Remove(group);
            }
        }

        if (rules.Disabled.TryGetValue(group, out var off))
        {
            off.Remove(name);
            if (off.Count == 0)
            {
                rules.Disabled.Remove(group);
            }
        }
    }

    /// <summary>Turns every rule of a group on or off (off stores every listed name, user names included).</summary>
    public static void SetGroupEnabled(UrlRules rules, RuleGroup group, bool enabled)
    {
        if (enabled)
        {
            rules.Disabled.Remove(group.Key);
            return;
        }

        rules.Disabled[group.Key] = new HashSet<string>(group.Names.Select(n => n.Name), StringComparer.Ordinal);
    }
}
