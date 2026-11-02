// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Maintenance;

/// <summary>Version comparison (port of the pure functions of AppUpdatesSupport): numeric aware, conservative.</summary>
internal static class VersionCompare
{
    /// <summary>Drops a leading "v" before a digit and everything after the first comma (package revisions).</summary>
    public static string Core(string? version)
    {
        var comparable = (version ?? string.Empty).Trim();
        if (comparable.Length > 1 && (comparable[0] == 'v' || comparable[0] == 'V') && char.IsDigit(comparable[1]))
        {
            comparable = comparable[1..];
        }

        var comma = comparable.IndexOf(',');
        return comma < 0 ? comparable : comparable[..comma];
    }

    /// <summary>Empty or "latest" cannot be compared.</summary>
    public static bool IsUncomparable(string? version)
    {
        var value = Core(version).ToLowerInvariant();
        return value.Length == 0 || value == "latest" || value == "unknown";
    }

    /// <summary>-1, 0 or 1.</summary>
    public static int Compare(string lhs, string rhs)
    {
        var left = Parts(lhs);
        var right = Parts(rhs);
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var a = i < left.Count ? left[i] : string.Empty;
            var b = i < right.Count ? right[i] : string.Empty;
            var result = ComparePart(a, b);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public static bool IsNewer(string candidate, string installed) => Compare(Core(candidate), Core(installed)) > 0;

    private static List<string> Parts(string version) =>
        [.. System.Text.RegularExpressions.Regex.Split(version, @"[^\p{L}\p{N}]+").Where(p => p.Length > 0)];

    private static bool AllDigits(string s) => s.Length > 0 && s.All(char.IsDigit);

    private static int ComparePart(string lhs, string rhs)
    {
        var leftDigits = AllDigits(lhs);
        var rightDigits = AllDigits(rhs);
        if (leftDigits && rightDigits)
        {
            return CompareDigits(lhs, rhs);
        }

        if (lhs.Length == 0)
        {
            return rightDigits && StripZeros(rhs).Length == 0 ? 0 : -1;
        }

        if (rhs.Length == 0)
        {
            return leftDigits && StripZeros(lhs).Length == 0 ? 0 : 1;
        }

        if (lhs == rhs)
        {
            return 0;
        }

        var leftNumber = new string(lhs.TakeWhile(char.IsDigit).ToArray());
        var rightNumber = new string(rhs.TakeWhile(char.IsDigit).ToArray());
        if ((leftNumber.Length == 0) != (rightNumber.Length == 0))
        {
            return leftNumber.Length == 0 ? -1 : 1;
        }

        if (leftNumber.Length > 0)
        {
            var numbers = CompareDigits(leftNumber, rightNumber);
            if (numbers != 0)
            {
                return numbers;
            }

            var leftBare = leftNumber.Length == lhs.Length;
            var rightBare = rightNumber.Length == rhs.Length;
            if (leftBare != rightBare)
            {
                return leftBare ? 1 : -1;
            }
        }

        var left = lhs.ToLowerInvariant();
        var right = rhs.ToLowerInvariant();
        if (left == right)
        {
            return 0;
        }

        var leftRuns = Runs(left);
        var rightRuns = Runs(right);
        for (var i = 0; i < Math.Min(leftRuns.Count, rightRuns.Count); i++)
        {
            var a = leftRuns[i];
            var b = rightRuns[i];
            if (a == b)
            {
                continue;
            }

            if (AllDigits(a) && AllDigits(b))
            {
                var numbers = CompareDigits(a, b);
                if (numbers != 0)
                {
                    return numbers;
                }
            }
            else
            {
                return string.CompareOrdinal(a, b) < 0 ? -1 : 1;
            }
        }

        if (leftRuns.Count == rightRuns.Count)
        {
            return 0;
        }

        return leftRuns.Count < rightRuns.Count ? -1 : 1;
    }

    private static List<string> Runs(string part)
    {
        var runs = new List<string>();
        foreach (var ch in part)
        {
            if (runs.Count > 0 && char.IsDigit(runs[^1][^1]) == char.IsDigit(ch))
            {
                runs[^1] += ch;
            }
            else
            {
                runs.Add(ch.ToString());
            }
        }

        return runs;
    }

    private static string StripZeros(string value) => value.TrimStart('0');

    private static int CompareDigits(string lhs, string rhs)
    {
        var a = StripZeros(lhs);
        var b = StripZeros(rhs);
        if (a.Length != b.Length)
        {
            return a.Length < b.Length ? -1 : 1;
        }

        var c = string.CompareOrdinal(a, b);
        return c == 0 ? 0 : c < 0 ? -1 : 1;
    }

    /// <summary>Self-check used by the test harness: returns the first failing expectation or null.</summary>
    public static string? SelfTest()
    {
        (string, string, int)[] cases =
        [
            ("1.2", "1.2.0", 0), ("1.2", "1.2.1", -1), ("130", "129", 1), ("9a", "10", -1), ("5", "5beta", 1),
            ("beta10", "beta9", 1), ("1.10", "1.9", 1), ("2.0.0", "2", 0), ("1.0.0-rc1", "1.0.0", 1),
        ];
        foreach (var (a, b, expected) in cases)
        {
            var actual = Compare(a, b);
            if (actual != expected)
            {
                return $"Compare({a}, {b}) = {actual}, expected {expected}";
            }
        }

        return null;
    }
}
