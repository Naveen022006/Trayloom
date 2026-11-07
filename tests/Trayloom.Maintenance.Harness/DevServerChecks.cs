// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Maintenance;

namespace Trayloom.Maintenance.Harness;

/// <summary>
/// Checks of the dev server rules: the filter, port classification, new-port detection, and the row URL. Pure logic; no process is
/// opened, listed or ended.
/// </summary>
internal static class DevServerChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Classification();
        Ports();
        Rows();
    }

    private static void Check(bool ok, string what) => _check(ok, "devservers: " + what);

    private static void Classification()
    {
        Check(DevServerRules.IsDevServer("node", 5173, "127.0.0.1", true, false, false), "node on 5173 at 127.0.0.1 is a dev server");
        Check(DevServerRules.IsDevServer("node", 5173, "127.0.0.1:5173", true, false, false), "an address with its port is read the same");
        Check(DevServerRules.IsDevServer("node", 3000, "*:3000", true, false, false), "any-interface counts");
        Check(DevServerRules.IsDevServer("node", 3000, "[::1]:3000", true, false, false), "IPv6 loopback counts");
        Check(!DevServerRules.IsDevServer("node", 3000, "192.168.1.20:3000", true, false, false), "a server bound to one network card does not");
        Check(!DevServerRules.IsDevServer("node", 3000, "127.0.0.1", false, false, false), "node on 3000 in another session is not");
        Check(!DevServerRules.IsDevServer("node", 3000, "127.0.0.1", true, true, false), "a protected process is not");
        Check(!DevServerRules.IsDevServer("node", 3000, "127.0.0.1", true, true, true), "a protected process is not, even with showAll");
        Check(!DevServerRules.IsDevServer("node", 443, "127.0.0.1", true, false, false), "port 443 is not");
        Check(!DevServerRules.IsDevServer("node", 1023, "127.0.0.1", true, false, true), "a privileged port is not, even with showAll");
        Check(!DevServerRules.IsDevServer("code", 3000, "127.0.0.1", true, false, false), "code on 3000 is not");
        Check(!DevServerRules.IsDevServer("Spotify", 4000, "127.0.0.1", true, false, false), "names are matched without case");
        Check(DevServerRules.IsDevServer("mytool", 5173, "127.0.0.1", true, false, false), "an unknown name on 5173 is");
        Check(!DevServerRules.IsDevServer("mytool", 12345, "127.0.0.1", true, false, false), "an unknown name on 12345 is not");
        Check(DevServerRules.IsDevServer("mytool", 12345, "127.0.0.1", true, false, true), "an unknown name on 12345 is with showAll");
        Check(DevServerRules.IsDevServer("Python", 12345, "127.0.0.1", true, false, false), "a runtime on any port is");
        Check(!DevServerRules.IsDevServer("code", 12345, "127.0.0.1", true, false, false), "code on an uncommon port is not");
    }

    private static void Ports()
    {
        Check(!DevServerRules.CommonPort(9229), "9229 (the Node inspector) is not a common port");
        Check(DevServerRules.CommonPort(3000) && DevServerRules.CommonPort(3010) && !DevServerRules.CommonPort(3011), "3000-3010");
        Check(DevServerRules.CommonPort(5173) && DevServerRules.CommonPort(5180) && !DevServerRules.CommonPort(5181), "5173-5180");
        Check(DevServerRules.CommonPort(8080) && DevServerRules.CommonPort(8090) && !DevServerRules.CommonPort(8091), "8080-8090");
        Check(DevServerRules.CommonPort(4173) && DevServerRules.CommonPort(6006) && DevServerRules.CommonPort(8443) && DevServerRules.CommonPort(9000), "single ports");
        Check(!DevServerRules.CommonPort(80) && !DevServerRules.CommonPort(445), "system ports are not");
    }

    private static PortEntry Entry(int port, string address, int pid = 4000, string name = "node") => new(port, "TCP", address, pid, name, 1);

    private static void Rows()
    {
        var before = new HashSet<int> { 3000, 5173 };
        var now = new[] { Entry(3000, "*:3000"), Entry(5173, "127.0.0.1:5173"), Entry(5174, "127.0.0.1:5174"), Entry(5174, "[::1]:5174"), Entry(4200, "*:4200") };
        var fresh = DevServerRules.NewPorts(before, now);
        Check(fresh.SequenceEqual([4200, 5174]), "NewPorts returns the unseen ports, once each, ascending");
        Check(DevServerRules.NewPorts(new HashSet<int> { 1 }, []).Count == 0, "NewPorts of nothing is nothing");

        var both = new[] { Entry(5173, "[::1]:5173", 100), Entry(5173, "127.0.0.1:5173", 200) };
        var one = DevServerRules.Filter(both, _ => true, false);
        Check(one.Count == 1 && one[0].Address == "127.0.0.1:5173", "one row per port, IPv4 preferred");
        var other = DevServerRules.Filter([Entry(5173, "[::1]:5173", 100), Entry(5173, "[::1]:5173", 50)], _ => true, false);
        Check(other.Count == 1 && other[0].Pid == 50, "equal address kinds keep the lower pid");
        var mixed = DevServerRules.Filter([Entry(9229, "127.0.0.1:9229"), Entry(3000, "127.0.0.1:3000", 7), Entry(3001, "127.0.0.1:3001", 8)], pid => pid != 8, false);
        Check(mixed.Count == 2 && mixed[0].Port == 3000 && mixed[1].Port == 9229, "Filter applies the session test and sorts by port (node on 9229 stays, being a runtime)");
        var showAll = DevServerRules.Filter([Entry(12345, "127.0.0.1:12345", 7, "mytool")], _ => true, true);
        Check(showAll.Count == 1, "showAll lists an uncommon port");
        var none = DevServerRules.Filter([Entry(12345, "127.0.0.1:12345", 7, "mytool")], _ => true, false);
        Check(none.Count == 0, "without showAll it is hidden");

        Check(Entry(3000, "*:3000").BrowserUrl == "http://localhost:3000", "URL of an any-interface listener");
        Check(Entry(5173, "127.0.0.1:5173").BrowserUrl == "http://127.0.0.1:5173", "URL of a loopback listener");
        Check(Entry(5173, "[::1]:5173").BrowserUrl == "http://[::1]:5173", "URL of an IPv6 loopback listener");
        Check(Sessions.IsMine(Environment.ProcessId), "the harness itself is in its own session");
        Check(!Sessions.IsMine(0), "pid 0 is nobody's");
    }
}
