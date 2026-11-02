// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Toolhelp / psapi entry points for the process list.</summary>
internal static class ProcNative
{
    public const uint TH32CS_SNAPPROCESS = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32FirstW(nint snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32NextW(nint snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] public static extern bool GetProcessMemoryInfo(nint process, out PROCESS_MEMORY_COUNTERS counters, uint size);
}

/// <summary>One row of the process list.</summary>
internal sealed record KillEntry(int Pid, int ParentPid, string Name, string Path, double Cpu, long Memory, bool IsRegularApp, int GroupedCount, bool IsProtected, long StartedAt, IReadOnlyList<(int Pid, long Started)> Members);

internal enum KillResult
{
    Killed,
    AlreadyGone,
    NeedsAdmin,
    Failed,
    Stale,
}

/// <summary>A process the user asked to end, captured with its identity.</summary>
internal readonly record struct KillTarget(int Pid, long StartedAt, string Name);

/// <summary>
/// Process snapshot + identity-verified termination (spec 6.4). A PID alone is reusable, so every kill re-verifies
/// <c>(pid, creation time)</c>.
/// </summary>
internal sealed class KillService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "system idle process", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "registry", "memory compression", "secure system", "dwm",
    };

    private static readonly HashSet<string> NotOwners = new(StringComparer.OrdinalIgnoreCase) { "explorer", "services", "wininit", "system", "svchost", "winlogon", "winknife" };

    private readonly Dictionary<(int, long), (long Ticks, long At)> _cpu = new();
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _refreshing;

    public static KillService Shared { get; } = new();

    public event Action? Changed;

    public List<KillEntry> Entries { get; private set; } = [];

    public bool HasLoadedOnce { get; private set; }

    public bool IsRefreshing => _refreshing != 0;

    public bool GroupRelated { get; set; } = true;

    /// <summary>The snapshot as a pid -> (parent, creation) table (descendant search).</summary>
    public Dictionary<int, (int Parent, long Created)> Table { get; private set; } = new();

    private static int SelfPid => Environment.ProcessId;

    /// <summary>The protected set: system processes, this app, and anything whose identity cannot be read.</summary>
    public static bool IsProtected(int pid, string shortName, long created) =>
        pid <= 4 || pid == SelfPid || created == 0 || ProtectedNames.Contains(shortName) || shortName.Equals("WinKnife", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refreshes the list; a fresh cache (3 s) makes this a no-op unless forced.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (!force && DateTime.Now - _lastRefresh < TimeSpan.FromSeconds(3))
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) != 0)
        {
            return;
        }

        Changed?.Invoke();
        try
        {
            var group = GroupRelated;
            var result = await Task.Run(() => Snapshot(group)).ConfigureAwait(true);
            if (result.Entries.Count > 0)
            {
                Entries = result.Entries;
                Table = result.Table;
                HasLoadedOnce = true;
            }

            _lastRefresh = DateTime.Now;
        }
        catch (Exception ex)
        {
            Host.Log("Process snapshot failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
            Changed?.Invoke();
        }
    }

    public void RemoveLocally(IEnumerable<int> pids)
    {
        var set = pids.ToHashSet();
        Entries = Entries.Where(e => !set.Contains(e.Pid)).ToList();
        Changed?.Invoke();
    }

    private (List<KillEntry> Entries, Dictionary<int, (int, long)> Table) Snapshot(bool group)
    {
        var raw = new List<(int Pid, int Ppid, string Exe)>();
        var snapshot = ProcNative.CreateToolhelp32Snapshot(ProcNative.TH32CS_SNAPPROCESS, 0);
        if (snapshot is 0 or -1)
        {
            return ([], new());
        }

        try
        {
            var entry = new ProcNative.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<ProcNative.PROCESSENTRY32W>() };
            if (ProcNative.Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    raw.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile));
                }
                while (ProcNative.Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            Win32.CloseHandle(snapshot);
        }

        var windowPids = VisibleWindowPids();
        var now = Stopwatch.GetTimestamp();
        var nowTicks = (long)(now * (10_000_000.0 / Stopwatch.Frequency));
        var rows = new List<KillEntry>();
        var table = new Dictionary<int, (int, long)>();
        var seen = new HashSet<(int, long)>();
        foreach (var (pid, ppid, exe) in raw)
        {
            if (pid <= 0)
            {
                continue;
            }

            long created = 0;
            long cpuTicks = 0;
            long memory = 0;
            string? path = null;
            var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle != 0)
            {
                try
                {
                    if (Win32.GetProcessTimes(handle, out created, out _, out var kernel, out var user))
                    {
                        cpuTicks = kernel + user;
                    }

                    if (ProcNative.GetProcessMemoryInfo(handle, out var counters, (uint)Marshal.SizeOf<ProcNative.PROCESS_MEMORY_COUNTERS>()))
                    {
                        memory = (long)counters.WorkingSetSize;
                    }

                    var builder = new System.Text.StringBuilder(1024);
                    var size = builder.Capacity;
                    if (Win32.QueryFullProcessImageNameW(handle, 0, builder, ref size))
                    {
                        path = builder.ToString();
                    }
                }
                finally
                {
                    Win32.CloseHandle(handle);
                }
            }

            table[pid] = (ppid, created);
            var key = (pid, created);
            seen.Add(key);
            var cpu = 0.0;
            if (_cpu.TryGetValue(key, out var previous) && nowTicks > previous.At)
            {
                cpu = Math.Max(0, (cpuTicks - previous.Ticks) * 100.0 / (nowTicks - previous.At));
            }

            _cpu[key] = (cpuTicks, nowTicks);
            var shortName = Path.GetFileNameWithoutExtension(exe);
            var isProtected = IsProtected(pid, shortName, created);
            rows.Add(new KillEntry(pid, ppid, DisplayName(path, shortName, pid), path ?? string.Empty, cpu, memory, windowPids.Contains(pid), 1, isProtected, created, [(pid, created)]));
        }

        foreach (var stale in _cpu.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _cpu.Remove(stale);
        }

        if (group)
        {
            rows = Group(rows, table);
        }

        rows = rows.OrderByDescending(r => r.Cpu).ThenBy(r => r.Pid).ToList();
        return (rows, table);
    }

    private string DisplayName(string? path, string shortName, int pid)
    {
        if (pid == 4)
        {
            return Loc.T("System");
        }

        if (pid == 0)
        {
            return Loc.T("System Idle Process");
        }

        if (string.IsNullOrEmpty(path))
        {
            return shortName;
        }

        if (!_descriptions.TryGetValue(path, out var description))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            }
            catch
            {
                description = null;
            }

            description = string.IsNullOrEmpty(description) || description.Length > 60 ? shortName : description;
            _descriptions[path] = description;
        }

        return description;
    }

    private static HashSet<int> VisibleWindowPids()
    {
        var set = new HashSet<int>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (Win32.IsWindowVisible(hwnd) && Win32.GetWindow(hwnd, 4) == 0 && (Win32.GetWindowLongPtr(hwnd, -20) & 0x80) == 0)
            {
                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                set.Add((int)pid);
            }

            return true;
        }, 0);
        return set;
    }

    private static List<KillEntry> Group(List<KillEntry> rows, Dictionary<int, (int Parent, long Created)> table)
    {
        var byPid = rows.ToDictionary(r => r.Pid);
        int Owner(KillEntry row)
        {
            var current = row;
            for (var guard = 0; guard < 32; guard++)
            {
                if (!byPid.TryGetValue(current.ParentPid, out var parent) || parent.Pid == current.Pid || parent.StartedAt == 0 || parent.StartedAt >= current.StartedAt || parent.IsProtected)
                {
                    break;
                }

                var parentName = System.IO.Path.GetFileNameWithoutExtension(parent.Path);
                if (NotOwners.Contains(parentName) || string.IsNullOrEmpty(parent.Path) || string.IsNullOrEmpty(current.Path))
                {
                    break;
                }

                var sameImage = string.Equals(parent.Path, current.Path, StringComparison.OrdinalIgnoreCase);
                var parentDir = System.IO.Path.GetDirectoryName(parent.Path) ?? "\0";
                if (!sameImage && !FileOps.IsUnder(current.Path, parentDir))
                {
                    break;
                }

                current = parent;
            }

            return current.Pid;
        }

        var groups = new Dictionary<int, List<KillEntry>>();
        foreach (var row in rows)
        {
            var owner = row.IsProtected ? row.Pid : Owner(row);
            if (!groups.TryGetValue(owner, out var list))
            {
                groups[owner] = list = [];
            }

            list.Add(row);
        }

        var result = new List<KillEntry>();
        foreach (var (owner, members) in groups)
        {
            if (!byPid.TryGetValue(owner, out var main))
            {
                continue;
            }

            if (members.Count == 1)
            {
                result.Add(main);
                continue;
            }

            result.Add(main with
            {
                Cpu = members.Sum(m => m.Cpu),
                Memory = members.Sum(m => m.Memory),
                GroupedCount = members.Count,
                IsRegularApp = members.Any(m => m.IsRegularApp),
                Members = members.Select(m => (m.Pid, m.StartedAt)).ToList(),
            });
        }

        return result;
    }

    // ------------------------------------------------------------------ killing

    /// <summary>Is this pid still the process we saw (creation time equals)?</summary>
    public static bool IsSameProcess(int pid, long startedAt) => startedAt != 0 && Win32.CreationTime(pid) == startedAt;

    private static bool HasTopLevelWindow(int pid)
    {
        var found = false;
        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid && Win32.IsWindowVisible(hwnd))
            {
                found = true;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    /// <summary>Ends one process (graceful = close request for windowed apps, otherwise terminate).</summary>
    public static KillResult KillOne(KillTarget target, bool force)
    {
        if (target.StartedAt == 0)
        {
            return KillResult.Stale;
        }

        var handle = Win32.OpenProcess(Win32.PROCESS_TERMINATE | Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.SYNCHRONIZE, false, target.Pid);
        if (handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            return error == 5 ? KillResult.NeedsAdmin : error == 87 ? KillResult.AlreadyGone : KillResult.Failed;
        }

        try
        {
            if (!Win32.GetProcessTimes(handle, out var creation, out _, out _, out _))
            {
                return KillResult.Failed;
            }

            if (creation != target.StartedAt)
            {
                return KillResult.Stale;
            }

            if (!force && HasTopLevelWindow(target.Pid))
            {
                Win32.EnumWindows((hwnd, _) =>
                {
                    Win32.GetWindowThreadProcessId(hwnd, out var owner);
                    if (owner == target.Pid && Win32.IsWindowVisible(hwnd))
                    {
                        Win32.PostMessageW(hwnd, Win32.WM_CLOSE, 0, 0);
                    }

                    return true;
                }, 0);
                return KillResult.Killed;
            }

            if (Win32.TerminateProcess(handle, 1))
            {
                return KillResult.Killed;
            }

            return Marshal.GetLastWin32Error() == 5 ? KillResult.NeedsAdmin : KillResult.Failed;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>Breadth-first descendants of <paramref name="root"/> (cap 4096), deepest first, root last.</summary>
    public IReadOnlyList<KillTarget> TreeOf(KillEntry root)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, (parent, created)) in Table)
        {
            if (Table.TryGetValue(parent, out var up) && created >= up.Created)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(pid);
            }
        }

        var order = new List<int>();
        var visited = new HashSet<int> { root.Pid };
        var queue = new Queue<int>();
        queue.Enqueue(root.Pid);
        while (queue.Count > 0 && order.Count < 4096)
        {
            var current = queue.Dequeue();
            if (!children.TryGetValue(current, out var list))
            {
                continue;
            }

            foreach (var child in list)
            {
                if (visited.Add(child))
                {
                    order.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        order.Reverse();
        var targets = new List<KillTarget>();
        foreach (var pid in order)
        {
            var protectedNames = Entries.FirstOrDefault(e => e.Pid == pid);
            if (protectedNames is { IsProtected: true } || pid <= 4 || pid == SelfPid)
            {
                continue;
            }

            targets.Add(new KillTarget(pid, Table[pid].Created, Name(pid)));
        }

        foreach (var (pid, started) in root.Members.Where(m => m.Pid != root.Pid))
        {
            targets.Add(new KillTarget(pid, started, root.Name));
        }

        targets.Add(new KillTarget(root.Pid, root.StartedAt, root.Name));
        return targets;
    }

    private static string Name(int pid) => Win32.ImagePath(pid) is { } path ? Path.GetFileNameWithoutExtension(path) : pid.ToString();

    /// <summary>
    /// Kills the targets, escalating once for the ones that need administrator rights. Returns true when no failure remained.
    /// </summary>
    public async Task<bool> KillAsync(IReadOnlyList<KillTarget> targets, bool force)
    {
        var results = await Task.Run(() => targets.Select(t => (Target: t, Result: KillOne(t, force))).ToList()).ConfigureAwait(true);
        var needAdmin = results.Where(r => r.Result == KillResult.NeedsAdmin).Select(r => r.Target).ToList();
        var failed = results.Count(r => r.Result == KillResult.Failed);
        if (needAdmin.Count > 0)
        {
            var name = needAdmin[0].Name;
            if (Dialogs.Confirm(Loc.F("WinKnife needs administrator access to end “{0}”.", name), string.Empty, Loc.T("Continue"), false))
            {
                var ok = await Task.Run(() => Elevated.Kill(needAdmin.Select(t => (t.Pid, t.StartedAt)).ToList())).ConfigureAwait(true);
                await Task.Delay(400).ConfigureAwait(true);
                failed += needAdmin.Count(t => IsSameProcess(t.Pid, t.StartedAt)) + (ok ? 0 : 0);
            }
            else
            {
                failed += needAdmin.Count;
            }
        }

        RemoveLocally(results.Where(r => r.Result is KillResult.Killed or KillResult.AlreadyGone).Select(r => r.Target.Pid));
        _ = Task.Delay(500).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAsync(true)));
        if (failed > 0)
        {
            Dialogs.Alert(Loc.T("Couldn’t Kill Process"), Loc.T("The process may have already exited or require additional privileges."));
        }

        return failed == 0;
    }

    /// <summary>Gracefully closes the process, waits for the real exit (10 s) and relaunches its image.</summary>
    public async Task RestartAsync(KillEntry entry)
    {
        if (entry.IsProtected || string.IsNullOrEmpty(entry.Path) || !entry.IsRegularApp || !File.Exists(entry.Path)
            || entry.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var exe = entry.Path;
        var dir = Path.GetDirectoryName(exe);
        var result = await Task.Run(() => KillOne(new KillTarget(entry.Pid, entry.StartedAt, entry.Name), false)).ConfigureAwait(true);
        if (result != KillResult.Killed)
        {
            return;
        }

        var exited = await Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(entry.Pid);
                return process.WaitForExit(10_000);
            }
            catch
            {
                return true;
            }
        }).ConfigureAwait(true);
        if (!exited)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir ?? string.Empty });
        }
        catch
        {
            // relaunch declined
        }

        await RefreshAsync(true).ConfigureAwait(true);
    }
}
