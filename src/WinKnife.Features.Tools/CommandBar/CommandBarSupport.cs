// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Tools;

/// <summary>Fuzzy matcher: exact > prefix > word match > subsequence with word-start and run bonuses.</summary>
internal static class Fuzzy
{
    public static int? Score(string query, string target)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        if (t == q)
        {
            return 1000;
        }

        if (t.StartsWith(q, StringComparison.Ordinal))
        {
            return 850 - Math.Min(100, t.Length - q.Length);
        }

        var index = t.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0)
        {
            var boundary = index == 0 || !char.IsLetterOrDigit(t[index - 1]);
            return (boundary ? 650 : 420) - Math.Min(100, index);
        }

        // Subsequence match.
        var score = 100;
        var ti = 0;
        var run = 0;
        foreach (var ch in q)
        {
            var found = -1;
            for (var i = ti; i < t.Length; i++)
            {
                if (t[i] == ch)
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            if (found == ti && ti > 0)
            {
                run++;
                score += 12 + run * 4;
            }
            else
            {
                run = 0;
                score -= Math.Min(15, found - ti);
            }

            if (found == 0 || !char.IsLetterOrDigit(t[found - 1]))
            {
                score += 25;
            }

            ti = found + 1;
        }

        return Math.Max(1, score);
    }

    /// <summary>Scores a multi-word query: every word must match the title or the secondary text.</summary>
    public static int? ScoreItem(string query, string title, string? secondary)
    {
        var total = 0;
        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = Score(token, title);
            if (s is null && secondary is not null)
            {
                s = Score(token, secondary) is { } sec ? sec / 3 : null;
            }

            if (s is null)
            {
                return null;
            }

            total += s.Value;
        }

        return total;
    }
}

/// <summary>An installed application discovered through the Start Menu shortcut folders.</summary>
internal sealed record AppEntry(string Name, string Path);

/// <summary>Indexes Start Menu shortcuts in the background and caches their icons.</summary>
internal sealed class AppIndex
{
    private readonly ConcurrentDictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    private int _scanning;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public DateTime LastScan { get; private set; } = DateTime.MinValue;

    public void RescanAsync()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var roots = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                };
                var found = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (var file in EnumerateShortcuts(root))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        found.TryAdd(name, new AppEntry(name, file));
                    }
                }

                _apps = found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                LastScan = DateTime.UtcNow;
            }
            catch
            {
                // keep the previous index
            }
            finally
            {
                Volatile.Write(ref _scanning, 0);
            }
        });
    }

    private static IEnumerable<string> EnumerateShortcuts(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        return Directory.EnumerateFiles(root, "*.lnk", options);
    }

    /// <summary>Returns the shell icon of a shortcut (cached). Must be called on the UI thread.</summary>
    public ImageSource? IconFor(string path)
    {
        if (_icons.TryGetValue(path, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                image = source;
            }
        }
        catch
        {
            // no icon
        }

        _icons[path] = image;
        return image;
    }
}

internal static class Calculator
{
    private static readonly Regex Allowed = new(@"^[\d\s\.,\+\-\*/%\(\)]+$", RegexOptions.Compiled);

    /// <summary>Evaluates a simple arithmetic expression; null when the text is not one.</summary>
    public static string? TryEvaluate(string text)
    {
        var expr = text.Trim().TrimEnd('=').Trim();
        if (expr.Length < 3 || !Allowed.IsMatch(expr) || !expr.Any(char.IsDigit) || !expr.Skip(1).Any(c => "+-*/%".Contains(c)))
        {
            return null;
        }

        try
        {
            expr = expr.Replace(',', '.');
            var value = new DataTable().Compute(expr, null);
            if (value is null or DBNull)
            {
                return null;
            }

            var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return null;
            }

            return Math.Round(d, 10).ToString("0.##########", CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
