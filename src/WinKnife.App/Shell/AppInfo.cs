// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Reflection;

namespace WinKnife.App;

/// <summary>Static facts about the running app (name, version, links).</summary>
internal static class AppInfo
{
    public const string Name = "WinKnife";

    /// <summary>Placeholder repository (WinKnife has no public repository yet).</summary>
    public const string RepositoryUrl = "https://github.com/winknife/winknife";

    public const string IssuesUrl = RepositoryUrl + "/issues/new";

    public static string Version
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        }
    }

    public static bool IsBeta => Version.Contains("-beta", StringComparison.OrdinalIgnoreCase)
        || Version.Contains("-rc", StringComparison.OrdinalIgnoreCase)
        || Version.Contains("-alpha", StringComparison.OrdinalIgnoreCase);

    public static string Copyright => "© 2026 WinKnife";
}
