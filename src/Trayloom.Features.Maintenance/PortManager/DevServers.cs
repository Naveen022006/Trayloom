// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace Trayloom.Features.Maintenance;

/// <summary>
/// Which listening sockets count as "a dev server" for the island page and its notice (spec 10e). Pure logic, so the harness can
/// check it without touching a process.
/// </summary>
internal static class DevServerRules
{
    /// <summary>Programs that normally run a dev server, whatever port they pick.</summary>
    public static readonly HashSet<string> Runtimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "deno", "bun", "python", "python3", "pythonw", "py", "dotnet", "java", "javaw", "ruby", "php", "php-cgi", "go", "hugo",
        "caddy", "iisexpress", "esbuild",
    };

    /// <summary>Apps that open local ports for their own plumbing (language servers, sync, chat), never a server the person started.</summary>
    public static readonly HashSet<string> NotServers = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "msedge", "chrome", "firefox", "discord", "spotify", "steam", "teams", "ms-teams", "slack", "claude", "onedrive", "svchost",
    };

    /// <summary>Ports the common dev tools default to. 9229 (the Node inspector) is deliberately absent: it is a debugger, not a site.</summary>
    public static bool CommonPort(int port) =>
        port is (>= 3000 and <= 3010) or 4000 or 4173 or 4200 or (>= 5000 and <= 5010) or (>= 5173 and <= 5180) or 5500 or 6006 or 7000
            or (>= 8000 and <= 8010) or (>= 8080 and <= 8090) or 8443 or 8888 or 9000;

    /// <summary>
    /// Whether one listener is a dev server. Only a process in the person's own session that is not protected qualifies, and only on a
    /// loopback or any-interface address above the privileged ports; then the name or the port must look like a dev server, unless
    /// <paramref name="showAll"/> asks for every listener of the person's apps.
    /// </summary>
    public static bool IsDevServer(string processName, int port, string address, bool sameSession, bool isProtected, bool showAll)
    {
        if (!sameSession || isProtected || port < 1024 || !IsLocalAddress(address))
        {
            return false;
        }

        if (showAll)
        {
            return true;
        }

        return !NotServers.Contains(processName) && (Runtimes.Contains(processName) || CommonPort(port));
    }

    /// <summary>
    /// The dev servers among <paramref name="all"/>, one row per port. A port reachable over IPv4 and IPv6 is one server to the person,
    /// so the IPv4 listener wins; ties go to the lower pid so the list does not flicker between polls.
    /// </summary>
    public static List<PortEntry> Filter(IEnumerable<PortEntry> all, Func<int, bool> sameSession, bool showAll)
    {
        var best = new Dictionary<int, PortEntry>();
        foreach (var entry in all)
        {
            if (!IsDevServer(entry.ProcessName, entry.Port, entry.Address, sameSession(entry.Pid), entry.IsProtected, showAll))
            {
                continue;
            }

            if (!best.TryGetValue(entry.Port, out var current) || Prefer(entry, current))
            {
                best[entry.Port] = entry;
            }
        }

        return [.. best.Values.OrderBy(e => e.Port)];
    }

    /// <summary>The ports of <paramref name="now"/> that were not in <paramref name="before"/>, ascending and without repeats.</summary>
    public static IReadOnlyList<int> NewPorts(IReadOnlySet<int> before, IEnumerable<PortEntry> now)
    {
        return [.. now.Select(e => e.Port).Where(p => !before.Contains(p)).Distinct().OrderBy(p => p)];
    }

    /// <summary>True for loopback and any-interface listeners; a server bound to one network card is not a local dev server.</summary>
    public static bool IsLocalAddress(string address)
    {
        var host = HostOf(address);
        return host is "*" or "0.0.0.0" or "::" or "::1" or "localhost" || host.StartsWith("127.", StringComparison.Ordinal);
    }

    /// <summary>The host part of "127.0.0.1:3000", "*:3000", "[::1]:3000" or a bare host.</summary>
    private static string HostOf(string address)
    {
        var text = address.Trim();
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            return close > 0 ? text[1..close] : text.Trim('[', ']');
        }

        // A single colon separates the port; several colons mean a bare IPv6 address.
        var colon = text.LastIndexOf(':');
        return colon > 0 && text.IndexOf(':') == colon ? text[..colon] : text;
    }

    private static bool Prefer(PortEntry candidate, PortEntry current)
    {
        var a = candidate.Address.StartsWith('[') ? 1 : 0;
        var b = current.Address.StartsWith('[') ? 1 : 0;
        return a != b ? a < b : candidate.Pid < current.Pid;
    }
}

/// <summary>Windows sessions of processes: a dev server of another signed-in account is none of this person's business.</summary>
internal static class Sessions
{
    private static readonly uint s_own = Own();

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    /// <summary>True when <paramref name="pid"/> runs in the same Windows session as Trayloom. False when it cannot be read.</summary>
    public static bool IsMine(int pid)
    {
        return pid > 0 && ProcessIdToSessionId((uint)pid, out var session) && session == s_own;
    }

    private static uint Own()
    {
        return ProcessIdToSessionId((uint)Environment.ProcessId, out var session) ? session : uint.MaxValue;
    }
}
