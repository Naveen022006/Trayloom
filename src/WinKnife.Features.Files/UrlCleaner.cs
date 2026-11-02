// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.RegularExpressions;

namespace WinKnife.Features.Files;

/// <summary>A cleaned link and the query parameter names taken out of it.</summary>
internal sealed record UrlCleanResult(string Url, IReadOnlyList<string> Removed);

/// <summary>User changes to the built-in rules: names added and built-in names switched off, per site ("" = every site).</summary>
internal sealed class UrlRules
{
    public Dictionary<string, HashSet<string>> Added { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, HashSet<string>> Disabled { get; } = new(StringComparer.Ordinal);

    public static UrlRules None { get; } = new();

    /// <summary>
    /// Builds rules from the three stored strings: global names (comma separated), <c>host|name</c> additions and
    /// <c>host|name</c> switch-offs (an empty host means every site, e.g. <c>|utm_*</c>).
    /// </summary>
    public static UrlRules Parse(string? globalNames, string? siteNames, string? disabledNames)
    {
        var rules = new UrlRules();
        foreach (var name in Split(globalNames))
        {
            Add(rules.Added, string.Empty, name);
        }

        foreach (var token in Split(siteNames))
        {
            AddToken(rules.Added, token);
        }

        foreach (var token in Split(disabledNames))
        {
            AddToken(rules.Disabled, token);
        }

        return rules;
    }

    private static void AddToken(Dictionary<string, HashSet<string>> map, string token)
    {
        var parts = token.Split('|', 2);
        if (parts.Length == 2 && parts[1].Trim().Length > 0)
        {
            Add(map, parts[0].Trim().ToLowerInvariant(), parts[1].Trim());
        }
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string site, string name)
    {
        if (!map.TryGetValue(site, out var set))
        {
            map[site] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(name.ToLowerInvariant());
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Strips tracking query parameters from http(s) links. Rule lists follow the reference implementation: a global list
/// (<c>utm_*</c>, click ids...) plus names that only count on specific sites (<c>si</c> on YouTube/Spotify, ...).
/// </summary>
internal static class UrlCleaner
{
    public const string UtmWildcard = "utm_*";

    internal static readonly HashSet<string> TrackedParameters = new(StringComparer.Ordinal)
    {
        "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid", "yclid",
        "mc_cid", "mc_eid", "igshid", "twclid", "ttclid", "li_fat_id",
        "mkt_tok", "_hsenc", "_hsmi", "__twitter_impression",
        "fb_action_ids", "fb_action_types", "fb_source", "mibextid",
    };

    /// <summary>Trackers that only one site uses (matched on the host or any subdomain of it).</summary>
    internal static readonly Dictionary<string, string[]> HostParameters = new(StringComparer.Ordinal)
    {
        ["youtube.com"] = ["si", "pp", "feature", "kw"],
        ["youtu.be"] = ["si", "pp", "feature", "kw"],
        ["twitter.com"] = ["s", "t", "cn", "src", "refsrc", "ref_src", "ref_url"],
        ["x.com"] = ["s", "t", "cn", "src", "refsrc", "ref_src", "ref_url"],
        ["instagram.com"] = ["igsh"],
        ["spotify.com"] = ["si"],
        ["reddit.com"] = ["correlation_id", "ref_campaign", "ref_source", "rdt", "share_id", "_branch_match_id", "$deep_link", "$3p", "$original_url"],
        ["tiktok.com"] = ["u_code", "preview_pb", "_d", "_t", "_r", "timestamp", "user_id", "share_app_name", "share_iid"],
        ["bilibili.com"] =
        [
            "spm_id_from", "from_spmid", "from_source", "share_source", "share_from", "share_medium", "share_plat",
            "share_tag", "share_session_id", "msource", "refer_from", "seid", "unique_k", "vd_source", "plat_id",
            "buvid", "bbid", "up_id", "is_story_h5", "timestamp", "ts", "visit_id", "session_id", "broadcast_type", "is_room_feed",
        ],
        ["xiaohongshu.com"] =
        [
            "xhsshare", "author_share", "xsec_source", "share_from_user_hidden", "shareredid", "share_id", "exsource",
            "app_version", "app_platform", "apptime", "appuid",
        ],
    };

    /// <summary>Cleans <paramref name="text"/> if it is a single http(s) link; null when it is not a link.</summary>
    public static UrlCleanResult? Clean(string text, UrlRules? rules = null)
    {
        rules ??= UrlRules.None;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var fragmentStart = trimmed.IndexOf('#');
        var beforeFragment = fragmentStart >= 0 ? trimmed[..fragmentStart] : trimmed;
        var queryStart = beforeFragment.IndexOf('?');
        if (queryStart < 0)
        {
            return new UrlCleanResult(trimmed, []);
        }

        var query = beforeFragment[(queryStart + 1)..];
        var matcher = BuildMatcher(uri.Host, rules);
        var removed = new List<string>();
        var kept = new List<string>();
        foreach (var pair in query.Split('&'))
        {
            var name = DecodedName(pair);
            if (matcher(name))
            {
                if (!removed.Contains(name))
                {
                    removed.Add(name);
                }
            }
            else
            {
                kept.Add(pair);
            }
        }

        if (removed.Count == 0)
        {
            return new UrlCleanResult(trimmed, []);
        }

        var cleaned = new StringBuilder(beforeFragment[..queryStart]);
        if (kept.Count > 0)
        {
            cleaned.Append('?').Append(string.Join('&', kept));
        }

        if (fragmentStart >= 0)
        {
            cleaned.Append(trimmed[fragmentStart..]);
        }

        return new UrlCleanResult(cleaned.ToString(), removed);
    }

    /// <summary>Canonical form used to compare two spellings of the same address.</summary>
    public static string Address(string link)
    {
        var value = link.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : value;
    }

    private static string DecodedName(string pair)
    {
        var equals = pair.IndexOf('=');
        var raw = equals >= 0 ? pair[..equals] : pair;
        if (!raw.Contains('%'))
        {
            return raw;
        }

        try
        {
            return Uri.UnescapeDataString(raw);
        }
        catch (Exception)
        {
            return raw;
        }
    }

    private static Func<string, bool> BuildMatcher(string host, UrlRules rules)
    {
        rules.Disabled.TryGetValue(string.Empty, out var disabledEverywhere);
        disabledEverywhere ??= [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in TrackedParameters)
        {
            if (!disabledEverywhere.Contains(name))
            {
                names.Add(name);
            }
        }

        if (rules.Added.TryGetValue(string.Empty, out var addedEverywhere))
        {
            foreach (var name in addedEverywhere)
            {
                if (!disabledEverywhere.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        var normalized = host.ToLowerInvariant();
        var sites = HostParameters.Keys.Concat(rules.Added.Keys).Concat(rules.Disabled.Keys)
            .Where(s => s.Length > 0 && (normalized == s || normalized.EndsWith("." + s, StringComparison.Ordinal)))
            .Distinct();
        foreach (var site in sites)
        {
            rules.Disabled.TryGetValue(site, out var disabled);
            if (HostParameters.TryGetValue(site, out var builtIn))
            {
                foreach (var name in builtIn)
                {
                    if (disabled is null || !disabled.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }

            if (rules.Added.TryGetValue(site, out var added))
            {
                foreach (var name in added)
                {
                    if (disabled is null || !disabled.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }

        var utm = !disabledEverywhere.Contains(UtmWildcard);
        return name =>
        {
            var lower = name.ToLowerInvariant();
            return names.Contains(lower) || (utm && lower.StartsWith("utm_", StringComparison.Ordinal));
        };
    }

    // ---- HTML flavour check -------------------------------------------------------------------------------------

    private static readonly Regex HiddenBlocks = new(
        @"<(head|style|script|title)\b[^<>]*>[\s\S]*?(?:</\1\s*>|(?=<body\b)|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Href = new(
        @"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Tags = new("<[^<>]*>", RegexOptions.Compiled);

    private static readonly string[] MediaTags = ["<img", "<video", "<audio", "<picture", "<svg", "<iframe", "<object", "<embed"];

    /// <summary>
    /// True when the copied HTML adds nothing to the bare link but formatting (every anchor leads to the link, no media),
    /// so replacing the clipboard with the cleaned plain link loses nothing.
    /// </summary>
    public static bool HtmlAddsNothing(string html, string link)
    {
        if (html.Length > 64 * 1024)
        {
            return false;
        }

        var shown = HiddenBlocks.Replace(html, string.Empty);
        var lower = html.ToLowerInvariant();
        if (MediaTags.Any(lower.Contains))
        {
            return false;
        }

        var linkAddress = Address(link);
        var targets = Href.Matches(shown)
            .Select(m => (m.Groups[1].Success ? m.Groups[1] : m.Groups[2].Success ? m.Groups[2] : m.Groups[3]).Value.Replace("&amp;", "&"))
            .Select(Address)
            .ToList();
        var visible = Tags.Replace(shown.Replace("&amp;", "&"), string.Empty).Trim();
        return targets.All(t => t == linkAddress) && (targets.Count > 0 || visible.Length == 0 || visible == link.Trim());
    }
}
