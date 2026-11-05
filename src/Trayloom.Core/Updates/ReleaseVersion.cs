// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trayloom.Core.Updates;

/// <summary>
/// A semantic version (SemVer 2.0, optional leading <c>v</c>, build metadata ignored). Pure code: no I/O.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string[] Pre) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>True for a version with a <c>-suffix</c> (alpha, beta, rc ...).</summary>
    public bool IsPrerelease => Pre is { Length: > 0 };

    /// <summary>Parses a tag or version text; false for anything that is not a valid SemVer (at most 64 characters).</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text) || text.Length > 64)
        {
            return false;
        }

        var match = Pattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }

        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        foreach (var part in pre)
        {
            // numeric identifiers have no leading zeros (SemVer 2.0 rule 9)
            if (part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        version = new ReleaseVersion(major, minor, patch, pre);
        return true;
    }

    /// <summary>SemVer precedence: core first; a release beats a prerelease; identifiers field by field.</summary>
    public int CompareTo(ReleaseVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        var a = Pre ?? [];
        var b = other.Pre ?? [];
        if (a.Length == 0 || b.Length == 0)
        {
            return a.Length == b.Length ? 0 : a.Length == 0 ? 1 : -1;
        }

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = a[i].All(char.IsAsciiDigit);
            var bNumeric = b[i].All(char.IsAsciiDigit);
            if (aNumeric && bNumeric)
            {
                // compare by length first so very long numbers cannot overflow
                var x = a[i].TrimStart('0');
                var y = b[i].TrimStart('0');
                c = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
            }
            else if (aNumeric != bNumeric)
            {
                c = aNumeric ? -1 : 1; // numbers rank below text
            }
            else
            {
                c = string.CompareOrdinal(a[i], b[i]);
            }

            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <inheritdoc />
    public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Pre ?? []));

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Pre) : string.Empty);
}

/// <summary>One published release. <see cref="Url"/> is built by Trayloom from the tag, never taken from the server's answer.</summary>
public sealed record ReleaseInfo(string Tag, ReleaseVersion Version, bool Prerelease, string Url);

/// <summary>Reads the GitHub releases list and picks the newest release the user should hear about.</summary>
public static class ReleasePicker
{
    /// <summary>Release list entries kept from one answer.</summary>
    public const int MaxEntries = 100;

    /// <summary>
    /// Reads <c>tag_name</c>, <c>draft</c> and <c>prerelease</c> of every entry and nothing else (the body and assets are ignored).
    /// Drafts and tags that are not a version are skipped. Returns an empty list for anything that is not a JSON array.
    /// </summary>
    public static List<ReleaseInfo> Parse(string json, string repoUrl)
    {
        var result = new List<ReleaseInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (result.Count >= MaxEntries)
                {
                    break;
                }

                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var prerelease = item.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
                if (FromTag(tagElement.GetString(), prerelease, repoUrl) is { } info)
                {
                    result.Add(info);
                }
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

    /// <summary>Builds a release from a tag (null when the tag is not a version). The link is made here from the tag.</summary>
    public static ReleaseInfo? FromTag(string? tag, bool prereleaseFlag, string repoUrl)
    {
        if (!ReleaseVersion.TryParse(tag, out var version))
        {
            return null;
        }

        return new ReleaseInfo(tag!, version, prereleaseFlag || version.IsPrerelease, repoUrl + "/releases/tag/" + Uri.EscapeDataString(tag!));
    }

    /// <summary>The newest release greater than <paramref name="current"/>; prereleases only when <paramref name="includePrerelease"/>.</summary>
    public static ReleaseInfo? Newest(IEnumerable<ReleaseInfo> releases, ReleaseVersion current, bool includePrerelease)
    {
        ReleaseInfo? best = null;
        foreach (var release in releases)
        {
            if ((release.Prerelease && !includePrerelease) || release.Version <= current)
            {
                continue;
            }

            if (best is null || release.Version > best.Version)
            {
                best = release;
            }
        }

        return best;
    }
}
