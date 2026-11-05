// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

/// <summary>
/// Decides which strings may be handed to the shell as a link. A path, a UNC share, a <c>file:</c> URL or a protocol handler such
/// as <c>ms-msdt:</c> or <c>search-ms:</c> would run or load something instead of opening a web page, so only a small set passes.
/// </summary>
public static class SafeLinks
{
    private const int MaxLength = 2048;

    private static readonly string[] s_shellPrefixes = ["ms-settings:", "windowsdefender:", "ms-windows-store:"];

    /// <summary>
    /// True for an absolute <c>http</c> or <c>https</c> URL (or <c>mailto:</c> when <paramref name="allowMailto"/>) with a host, no
    /// user-info, no whitespace or control character and at most 2048 characters. <paramref name="uri"/> is null when false.
    /// </summary>
    public static bool IsWebUrl(string? text, out Uri? uri, bool allowMailto = true)
    {
        uri = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || HasWhitespaceOrControl(text))
        {
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var scheme = parsed.Scheme;
        var web = scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps;
        if (web)
        {
            if (string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            {
                return false;
            }
        }
        else if (!(allowMailto && scheme == Uri.UriSchemeMailto))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// True for a web URL, or for one of the fixed Windows schemes Trayloom opens itself: <c>ms-settings:</c>, <c>windowsdefender:</c>
    /// and <c>ms-windows-store:</c>.
    /// </summary>
    public static bool IsAllowedShellUri(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        if (IsWebUrl(text, out _))
        {
            return true;
        }

        if (HasWhitespaceOrControl(text))
        {
            return false;
        }

        foreach (var prefix in s_shellPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWhitespaceOrControl(string text)
    {
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
