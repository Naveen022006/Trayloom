// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Trayloom.Features.Agents;

/// <summary>A process as the session monitor needs it: who it is, whose child it is and when it started.</summary>
internal readonly record struct AgentProcess(int Pid, int Parent, string Name, DateTime? Started);

/// <summary>Read-only process and window lookups: liveness, children, ancestors and the window of an agent's host.</summary>
internal static class AgentProcesses
{
    private const uint Snapshot = 0x2;
    private const uint QueryLimited = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    /// <summary>Every running process with its parent, name and start time.</summary>
    public static List<AgentProcess> All()
    {
        var list = new List<AgentProcess>();
        var snapshot = CreateToolhelp32Snapshot(Snapshot, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return list;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                list.Add(new AgentProcess((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile ?? "", null));
            }
        }
        finally
        {
            AgentNative.CloseHandle(snapshot);
        }

        return list;
    }

    /// <summary>When the process started (UTC); null when it cannot be opened or has gone.</summary>
    public static DateTime? StartTime(int pid)
    {
        var handle = AgentNative.OpenProcess(QueryLimited, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessTimes(handle, out var created, out _, out _, out _) && created > 0 ? DateTime.FromFileTimeUtc(created) : null;
        }
        finally
        {
            AgentNative.CloseHandle(handle);
        }
    }

    /// <summary>Whether <paramref name="pid"/> runs and is the process the record meant (a reused pid starts at another time).</summary>
    public static bool IsSameProcess(int pid, long? recordedStartFileTime)
    {
        if (!AgentSessionRegistry.IsRunning(pid))
        {
            return false;
        }

        if (recordedStartFileTime is not { } recorded || StartTime(pid) is not { } started)
        {
            return true;
        }

        return Math.Abs((started - DateTime.FromFileTimeUtc(recorded)).TotalSeconds) < 5;
    }

    /// <summary>Descendants of <paramref name="pid"/> that started at or after <paramref name="since"/> (a tool the agent ran after its last write).</summary>
    public static List<AgentProcess> ChildrenStartedSince(List<AgentProcess> all, int pid, DateTime since)
    {
        var result = new List<AgentProcess>();
        var queue = new Queue<int>();
        queue.Enqueue(pid);
        var seen = new HashSet<int> { pid };
        while (queue.Count > 0 && result.Count < 8)
        {
            var parent = queue.Dequeue();
            foreach (var p in all.Where(p => p.Parent == parent && seen.Add(p.Pid)))
            {
                if (StartTime(p.Pid) is { } started && started >= since - TimeSpan.FromSeconds(2))
                {
                    result.Add(p with { Started = started });
                }

                queue.Enqueue(p.Pid);
            }
        }

        return result;
    }

    /// <summary>The processes above <paramref name="pid"/>, nearest first.</summary>
    public static List<AgentProcess> Ancestors(List<AgentProcess> all, int pid)
    {
        var map = all.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        var result = new List<AgentProcess>();
        var seen = new HashSet<int> { pid };
        while (map.TryGetValue(pid, out var p) && map.TryGetValue(p.Parent, out var parent) && seen.Add(parent.Pid) && result.Count < 12)
        {
            result.Add(parent);
            pid = parent.Pid;
        }

        return result;
    }

    /// <summary>Visible top-level windows with a title: (handle, owner pid, title, class).</summary>
    public static List<(IntPtr Handle, int Pid, string Title, string Class)> Windows()
    {
        var list = new List<(IntPtr, int, string, string)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero || (GetWindowLong(hwnd, -20) & 0x80) != 0)
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowTextW(hwnd, title, 256);
            if (title.Length == 0)
            {
                return true;
            }

            var cls = new StringBuilder(128);
            GetClassNameW(hwnd, cls, 128);
            GetWindowThreadProcessId(hwnd, out var pid);
            list.Add((hwnd, (int)pid, title.ToString(), cls.ToString()));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Brings the window that hosts an agent session forward: the Claude app for sessions it started, otherwise the terminal or editor
    /// above the agent's process. No input is sent; the click on the island is what lets the window take the foreground.
    /// </summary>
    public static bool FocusHost(AgentSession session)
    {
        try
        {
            var windows = Windows();
            var all = All();
            IntPtr target = IntPtr.Zero;
            if (session.Origin == "Claude app")
            {
                foreach (var w in windows)
                {
                    if (w.Class == "Chrome_WidgetWin_1" && ProcessName(all, w.Pid).Equals("claude.exe", StringComparison.OrdinalIgnoreCase)
                        && w.Title.StartsWith("Claude", StringComparison.OrdinalIgnoreCase))
                    {
                        target = w.Handle;
                        break;
                    }
                }
            }

            if (target == IntPtr.Zero && session.Pid is { } pid)
            {
                foreach (var ancestor in Ancestors(all, pid))
                {
                    var w = windows.FirstOrDefault(x => x.Pid == ancestor.Pid);
                    if (w.Handle != IntPtr.Zero)
                    {
                        target = w.Handle;
                        break;
                    }
                }
            }

            if (target == IntPtr.Zero)
            {
                return false;
            }

            if (IsIconic(target))
            {
                ShowWindow(target, 9);
            }

            BringWindowToTop(target);
            return SetForegroundWindow(target);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string ProcessName(List<AgentProcess> all, int pid) => all.FirstOrDefault(p => p.Pid == pid).Name ?? "";
}
