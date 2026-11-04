// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// Developer aid (TRAYLOOM_DEV=1 only, spec 07 section 11.1): process names (lower-case, no extension) listed in
/// <c>away-mode-only.txt</c>. When the file exists, only windows of those processes are ever touched. A browser name additionally needs
/// the browser to run on a profile folder (<c>--user-data-dir=</c> or <c>-profile</c>) under the Trayloom data folder or the temp
/// folder, so a test can never reach the person's own browser, whose profile lives in the user's AppData.
/// </summary>
internal sealed class AwayDevFilter
{
    private static readonly string[] BrowserNames = ["chrome", "msedge", "brave", "vivaldi", "opera", "firefox"];
    private static readonly ConcurrentDictionary<uint, (long Start, bool Allowed)> Cache = new();

    private readonly HashSet<string> _names;
    private readonly string[] _roots;

    private AwayDevFilter(IEnumerable<string> names)
    {
        _names = [.. names];
        var roots = new List<string>();
        AddRoot(roots, Environment.GetEnvironmentVariable("TRAYLOOM_DATA"));
        AddRoot(roots, Path.GetTempPath());
        _roots = [.. roots];
    }

    /// <summary>The filter, or null when the restriction is not active.</summary>
    public static AwayDevFilter? Load()
    {
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") != "1")
        {
            return null;
        }

        try
        {
            var path = Path.Combine(AppPaths.Root, "away-mode-only.txt");
            if (!File.Exists(path))
            {
                return null;
            }

            return new AwayDevFilter(File.ReadAllLines(path).Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when the process may be touched while the restriction is active.</summary>
    public bool Allows(uint pid, string processName)
    {
        if (!_names.Contains(processName))
        {
            return false;
        }

        if (Array.IndexOf(BrowserNames, processName) < 0)
        {
            return true;
        }

        var start = AwayProcess.StartTime(pid);
        if (Cache.TryGetValue(pid, out var known) && known.Start == start && start != 0)
        {
            return known.Allowed;
        }

        var allowed = start != 0 && ProfileUnderTestRoots(pid);
        if (Cache.Count > 500)
        {
            Cache.Clear();
        }

        Cache[pid] = (start, allowed);
        return allowed;
    }

    private bool ProfileUnderTestRoots(uint pid)
    {
        var command = AwayProcess.CommandLine(pid);
        if (command is null)
        {
            return false;
        }

        var folder = AwayProcess.SwitchValue(command, "--user-data-dir") ?? AwayProcess.SwitchValue(command, "-profile");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return _roots.Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            roots.Add(Path.GetFullPath(path).TrimEnd('\\') + "\\");
        }
        catch (Exception)
        {
            // an unusable root simply allows nothing
        }
    }
}
