// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;

namespace Trayloom.Features.Tools;

/// <summary>One emoji row source.</summary>
/// <param name="Glyph">The emoji (with VS16 when text-default).</param>
/// <param name="Name">Lower-case Unicode-style name ("grinning face").</param>
/// <param name="Keywords">Extra search words (aliases), lower case.</param>
/// <param name="CanTone">Takes a skin-tone modifier.</param>
internal sealed record EmojiEntry(string Glyph, string Name, string Keywords, bool CanTone);

/// <summary>Emoji pool, aliases and skin tones (spec 1.7.5).</summary>
internal static class CommandBarEmoji
{
    private static readonly Dictionary<int, string> Aliases = new()
    {
        [0x1F602] = "haha lol roflmao laugh laughing tears funny",
        [0x1F923] = "haha lol rofl roflmao laugh laughing funny",
        [0x1F60A] = "happy smile blush",
        [0x1F970] = "love affection hearts",
        [0x1F618] = "kiss love",
        [0x1F60E] = "cool sunglasses",
        [0x1F914] = "think thinking hmm",
        [0x1F644] = "eyeroll whatever",
        [0x1F62D] = "cry crying sad sob",
        [0x1F97A] = "please pleading puppy eyes",
        [0x1F621] = "angry mad rage",
        [0x1F92C] = "swear cursing angry",
        [0x1F937] = "idk shrug whatever",
        [0x1F480] = "dead death dying skeleton halloween",
        [0x2620] = "dead death danger poison pirate",
        [0x1F64F] = "appreciate please thanks thank thx you pray prayer high five",
        [0x1F44D] = "yes good approve like okay",
        [0x1F44E] = "no bad disapprove dislike",
        [0x1F44C] = "okay perfect good",
        [0x1F44F] = "clap applause congrats congratulations",
        [0x1F64C] = "hooray celebrate praise",
        [0x1FAF6] = "love heart hands",
        [0x1F440] = "look looking eyes see",
        [0x2764] = "love heart red",
        [0x1F494] = "heartbreak broken heart sad",
        [0x1F525] = "fire hot lit trending",
        [0x2728] = "sparkle sparkles magic clean",
        [0x1F389] = "party celebrate celebration congrats congratulations",
        [0x2705] = "check done yes complete success",
        [0x274C] = "cross no wrong error fail",
        [0x26A0] = "warning caution alert",
        [0x1F4A1] = "idea lightbulb tip",
        [0x1F680] = "launch ship rocket fast",
    };

    private static readonly (int From, int To)[] TextDefault =
    [
        (0x203C, 0x203C), (0x2049, 0x2049), (0x2122, 0x2122), (0x2139, 0x2139), (0x2194, 0x2199), (0x21A9, 0x21AA), (0x2328, 0x2328),
        (0x23CF, 0x23CF), (0x23ED, 0x23EF), (0x23F1, 0x23F2), (0x23F8, 0x23FA), (0x24C2, 0x24C2), (0x25AA, 0x25AB), (0x25B6, 0x25B6),
        (0x25C0, 0x25C0), (0x25FB, 0x25FC), (0x2600, 0x2604), (0x260E, 0x260E), (0x2611, 0x2611), (0x2618, 0x2618), (0x261D, 0x261D),
        (0x2620, 0x2620), (0x2622, 0x2623), (0x2626, 0x2626), (0x262A, 0x262A), (0x262E, 0x262F), (0x2638, 0x263A), (0x2640, 0x2640),
        (0x2642, 0x2642), (0x265F, 0x2660), (0x2663, 0x2663), (0x2665, 0x2666), (0x2668, 0x2668), (0x267B, 0x267B), (0x267E, 0x267E),
        (0x2692, 0x2692), (0x2694, 0x2697), (0x2699, 0x2699), (0x269B, 0x269C), (0x26A0, 0x26A0), (0x26A7, 0x26A7), (0x26B0, 0x26B1),
        (0x26C8, 0x26C8), (0x26CF, 0x26CF), (0x26D1, 0x26D1), (0x26D3, 0x26D3), (0x26E9, 0x26E9), (0x26F0, 0x26F1), (0x26F4, 0x26F4),
        (0x26F7, 0x26F9), (0x2702, 0x2702), (0x2708, 0x2709), (0x270C, 0x270D), (0x270F, 0x270F), (0x2712, 0x2712), (0x2714, 0x2714),
        (0x2716, 0x2716), (0x271D, 0x271D), (0x2721, 0x2721), (0x2733, 0x2734), (0x2744, 0x2744), (0x2747, 0x2747), (0x2763, 0x2764),
        (0x27A1, 0x27A1), (0x2934, 0x2935), (0x2B05, 0x2B07), (0x3030, 0x3030), (0x303D, 0x303D), (0x3297, 0x3297), (0x3299, 0x3299),
        (0xA9, 0xA9), (0xAE, 0xAE), (0x1F170, 0x1F171), (0x1F17E, 0x1F17F), (0x1F321, 0x1F321), (0x1F324, 0x1F32C), (0x1F336, 0x1F336),
        (0x1F37D, 0x1F37D), (0x1F396, 0x1F397), (0x1F399, 0x1F39B), (0x1F39E, 0x1F39F), (0x1F3CB, 0x1F3CE), (0x1F3D4, 0x1F3DF),
        (0x1F3F3, 0x1F3F3), (0x1F3F5, 0x1F3F5), (0x1F3F7, 0x1F3F7), (0x1F43F, 0x1F43F), (0x1F441, 0x1F441), (0x1F4FD, 0x1F4FD),
        (0x1F549, 0x1F54A), (0x1F56F, 0x1F570), (0x1F573, 0x1F579), (0x1F587, 0x1F587), (0x1F58A, 0x1F58D), (0x1F590, 0x1F590),
        (0x1F5A5, 0x1F5A5), (0x1F5A8, 0x1F5A8), (0x1F5B1, 0x1F5B2), (0x1F5BC, 0x1F5BC), (0x1F5C2, 0x1F5C4), (0x1F5D1, 0x1F5D3),
        (0x1F5DC, 0x1F5DE), (0x1F5E1, 0x1F5E1), (0x1F5E3, 0x1F5E3), (0x1F5E8, 0x1F5E8), (0x1F5EF, 0x1F5EF), (0x1F5F3, 0x1F5F3),
        (0x1F5FA, 0x1F5FA), (0x1F6CB, 0x1F6CB), (0x1F6CD, 0x1F6CF), (0x1F6E0, 0x1F6E5), (0x1F6E9, 0x1F6E9), (0x1F6F0, 0x1F6F0),
        (0x1F6F3, 0x1F6F3),
    ];

    private static readonly (int From, int To)[] ToneBases =
    [
        (0x261D, 0x261D), (0x26F9, 0x26F9), (0x270A, 0x270D), (0x1F385, 0x1F385), (0x1F3C2, 0x1F3C4), (0x1F3C7, 0x1F3C7), (0x1F3CA, 0x1F3CC),
        (0x1F442, 0x1F443), (0x1F446, 0x1F450), (0x1F466, 0x1F469), (0x1F46B, 0x1F478), (0x1F47C, 0x1F47C), (0x1F481, 0x1F483),
        (0x1F485, 0x1F487), (0x1F4AA, 0x1F4AA), (0x1F574, 0x1F575), (0x1F57A, 0x1F57A), (0x1F590, 0x1F590), (0x1F595, 0x1F596),
        (0x1F645, 0x1F647), (0x1F64B, 0x1F64F), (0x1F6A3, 0x1F6A3), (0x1F6B4, 0x1F6B6), (0x1F6C0, 0x1F6C0), (0x1F6CC, 0x1F6CC),
        (0x1F90C, 0x1F90C), (0x1F90F, 0x1F90F), (0x1F918, 0x1F91F), (0x1F926, 0x1F926), (0x1F930, 0x1F939), (0x1F93C, 0x1F93E),
        (0x1F977, 0x1F977), (0x1F9B5, 0x1F9B6), (0x1F9B8, 0x1F9B9), (0x1F9BB, 0x1F9BB), (0x1F9CD, 0x1F9CF), (0x1F9D1, 0x1F9DD),
        (0x1FAC3, 0x1FAC5), (0x1FAF0, 0x1FAF8),
    ];

    private static readonly Lazy<IReadOnlyList<EmojiEntry>> Pool = new(Load);

    /// <summary>Popular emoji first (fixed order), then the rest sorted by name.</summary>
    public static IReadOnlyList<EmojiEntry> All => Pool.Value;

    /// <summary>Applies a tone ("", light, mediumLight, medium, mediumDark, dark) to a glyph when it can take one.</summary>
    public static string ApplyTone(EmojiEntry entry, string tone)
    {
        var modifier = tone switch
        {
            "light" => 0x1F3FB,
            "mediumLight" => 0x1F3FC,
            "medium" => 0x1F3FD,
            "mediumDark" => 0x1F3FE,
            "dark" => 0x1F3FF,
            _ => 0,
        };
        if (modifier == 0 || !entry.CanTone)
        {
            return entry.Glyph;
        }

        return Canonical(entry.Glyph) + char.ConvertFromUtf32(modifier);
    }

    /// <summary>The glyph without variation selectors.</summary>
    public static string Canonical(string glyph) => glyph.Replace("️", string.Empty).Replace("︎", string.Empty);

    private static bool InRanges((int From, int To)[] ranges, int scalar)
    {
        foreach (var (from, to) in ranges)
        {
            if (scalar >= from && scalar <= to)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<EmojiEntry> Load()
    {
        var byGlyph = new Dictionary<string, EmojiEntry>(StringComparer.Ordinal);
        foreach (var raw in CommandBarEmojiData.Table.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0)
            {
                continue;
            }

            var canonical = Canonical(line[..space]);
            var scalars = canonical.EnumerateRunes().ToArray();
            if (scalars.Length != 1)
            {
                continue;
            }

            var scalar = scalars[0].Value;
            var glyph = InRanges(TextDefault, scalar) ? canonical + "️" : canonical;
            var name = line[(space + 1)..].Trim().ToLowerInvariant();
            var keywords = Aliases.TryGetValue(scalar, out var alias) ? alias : string.Empty;
            var tone = scalar != 0x1F46A && InRanges(ToneBases, scalar);
            byGlyph.TryAdd(canonical, new EmojiEntry(glyph, name, keywords, tone));
        }

        var result = new List<EmojiEntry>(byGlyph.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rune in CommandBarEmojiData.Popular.EnumerateRunes())
        {
            var key = rune.ToString();
            if (byGlyph.TryGetValue(key, out var entry) && used.Add(key))
            {
                result.Add(entry);
            }
        }

        foreach (var entry in byGlyph.Values.Where(e => !used.Contains(Canonical(e.Glyph))).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            result.Add(entry);
        }

        return result;
    }
}
