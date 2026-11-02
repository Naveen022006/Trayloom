// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinKnife.Features.Agents;

/// <summary>The few Win32 calls the agents project needs.</summary>
internal static class AgentNative
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(IntPtr process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);

    /// <summary>(VolumeSerialNumber, FileIndex) folded into one value; replaces the inode.</summary>
    public static ulong FileIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            return 0;
        }

        return ((ulong)info.FileIndexHigh << 32 | info.FileIndexLow) ^ ((ulong)info.VolumeSerialNumber << 48);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong time);

    /// <summary>Seconds the PC has been running awake (sleep time left out), for offline detection.</summary>
    public static double AwakeSeconds => QueryUnbiasedInterruptTime(out var t) ? t / 10_000_000.0 : Environment.TickCount64 / 1000.0;

    public static uint LinkCount(SafeFileHandle handle) => GetFileInformationByHandle(handle, out var info) ? info.NumberOfLinks : 0;

    // Job object: the CLIs we start die with WinKnife.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static IntPtr _job;
    private static readonly object JobGate = new();

    /// <summary>Puts <paramref name="process"/> in a kill-on-close job so it dies with WinKnife. Best effort.</summary>
    public static void BindToJob(System.Diagnostics.Process process)
    {
        try
        {
            lock (JobGate)
            {
                if (_job == IntPtr.Zero)
                {
                    var job = CreateJobObjectW(IntPtr.Zero, null);
                    if (job == IntPtr.Zero)
                    {
                        return;
                    }

                    var info = new JobObjectExtendedLimitInformation();
                    info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
                    {
                        CloseHandle(job);
                        return;
                    }

                    _job = job;
                }

                AssignProcessToJobObject(_job, process.Handle);
            }
        }
        catch (Exception)
        {
            // A process already in another job (nested jobs are fine on Windows 8+) or an exited one: nothing to do.
        }
    }
}

/// <summary>Replies still being written, by id, and the rows read last, for a database.</summary>
internal sealed class AgentOpenCodeProgress
{
    public Dictionary<string, (long Updated, long Length, DateTime Changed)> Open { get; } = new(StringComparer.Ordinal);

    public List<(long RowId, string Id)> Tail { get; } = [];

    public void Clear()
    {
        Open.Clear();
        Tail.Clear();
    }
}

/// <summary>How far reading one log file has got.</summary>
internal sealed class AgentLogCursor
{
    public AgentLogCursor(string path, AgentProvider provider)
    {
        Path = path;
        Provider = provider;
        Parent = provider == AgentProvider.Claude ? ParentOf(path) : null;
    }

    public string Path { get; }

    public AgentProvider Provider { get; }

    /// <summary>The session log a Claude subagent works for.</summary>
    public string? Parent { get; }

    public AgentOpenCodeProgress OpenCode { get; set; } = new();

    public ulong Offset { get; set; }

    public ulong Identity { get; set; }

    public byte[] Pending { get; set; } = [];

    public bool Discarding { get; set; }

    public AgentLogState State { get; set; } = new();

    public DateTime Modified { get; set; } = DateTime.MinValue;

    /// <summary>Set when the log turned out replaced, cut short or written again after part of it was read.</summary>
    public bool Restarted { get; private set; }

    /// <summary>Subagents report to a turn another file tracks.</summary>
    public bool TracksTurns => Provider == AgentProvider.Claude ? Parent is null : Provider != AgentProvider.Codex || !State.CodexSubagent;

    private string? _codexParentLog;
    private int _codexParentSearch = -1;
    private (ulong Offset, ulong Identity, ulong Value)? _fingerprinted;

    /// <summary>The log whose turn a Codex subagent's responses count toward.</summary>
    public string? CodexParent(Dictionary<string, AgentLogCursor> cursors)
    {
        if (Provider != AgentProvider.Codex || State.CodexRoot.Length == 0 || TracksTurns)
        {
            return null;
        }

        if (_codexParentSearch != cursors.Count || (_codexParentLog is not null && !cursors.ContainsKey(_codexParentLog)))
        {
            _codexParentSearch = cursors.Count;
            _codexParentLog = cursors.Values
                .Where(c => c.Provider == AgentProvider.Codex && c.TracksTurns && c.State.Session == State.CodexRoot)
                .OrderByDescending(c => c.Modified)
                .FirstOrDefault()?.Path;
        }

        return _codexParentLog;
    }

    /// <summary>Where a resumed read picks up: the start of a line still being written, or past a line too long to keep.</summary>
    public ulong Boundary => Discarding ? Offset : Offset - (ulong)Pending.Length;

    public void StartOver(ulong identity)
    {
        if (Offset > 0)
        {
            Restarted = true;
        }

        Identity = identity;
        Offset = 0;
        Pending = [];
        Discarding = false;
        State = new AgentLogState();
        _fingerprinted = null;
        _codexParentSearch = -1;
    }

    public void FingerprintRead(SafeFileHandle handle)
    {
        var end = Boundary;
        if (_fingerprinted is { } taken && taken.Offset == end && taken.Identity == Identity)
        {
            return;
        }

        var value = AgentLogReader.Fingerprint(handle, end);
        _fingerprinted = value is { } v ? (end, Identity, v) : null;
    }

    /// <summary>False once the log no longer holds what was read.</summary>
    public bool HoldsWhatWasRead(SafeFileHandle handle)
    {
        if (_fingerprinted is not { } taken || taken.Identity != Identity)
        {
            return true;
        }

        return AgentLogReader.Fingerprint(handle, taken.Offset) == taken.Value;
    }

    public sealed class SavedCursor
    {
        public string Path { get; set; } = "";

        public AgentProvider Provider { get; set; }

        public ulong Offset { get; set; }

        public ulong Identity { get; set; }

        public bool Discarding { get; set; }

        public DateTime Modified { get; set; }

        public AgentLogState State { get; set; } = new();

        public ulong Fingerprint { get; set; }
    }

    public SavedCursor Save(SafeFileHandle? handle)
    {
        var end = Boundary;
        ulong fp;
        if (_fingerprinted is { } taken && taken.Offset == end && taken.Identity == Identity)
        {
            fp = taken.Value;
        }
        else
        {
            fp = (handle is not null ? AgentLogReader.Fingerprint(handle, end) : null) ?? 0;
            _fingerprinted = (end, Identity, fp);
        }

        return new SavedCursor { Path = Path, Provider = Provider, Offset = end, Identity = Identity, Discarding = Discarding, Modified = Modified, State = State, Fingerprint = fp };
    }

    /// <summary>Null when the path no longer holds the log that was read up to the saved offset.</summary>
    public static AgentLogCursor? Restore(SavedCursor saved)
    {
        try
        {
            using var handle = File.OpenHandle(saved.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (AgentNative.FileIdentity(handle) != saved.Identity || AgentLogReader.Fingerprint(handle, saved.Offset) != saved.Fingerprint)
            {
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new AgentLogCursor(saved.Path, saved.Provider)
        {
            Offset = saved.Offset,
            Identity = saved.Identity,
            Discarding = saved.Discarding,
            Modified = saved.Modified,
            State = saved.State,
            _fingerprinted = (saved.Offset, saved.Identity, saved.Fingerprint),
        };
    }

    /// <summary>Claude Code keeps a session's subagents in <c>&lt;session&gt;\subagents\</c>, beside the session's own <c>&lt;session&gt;.jsonl</c>.</summary>
    public static string? ParentOf(string path)
    {
        var a = path.LastIndexOf("\\subagents\\", StringComparison.OrdinalIgnoreCase);
        var b = path.LastIndexOf("/subagents/", StringComparison.OrdinalIgnoreCase);
        var i = Math.Max(a, b);
        return i < 0 ? null : path[..i] + ".jsonl";
    }
}

/// <summary>Where each agent keeps its session logs.</summary>
internal sealed record AgentLogRoot(AgentProvider Provider, string Path)
{
    public bool Exists => Directory.Exists(Path);

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary><c>%LOCALAPPDATA%WinKnife</c> (the WINKNIFE_LOCALDATA environment variable overrides it for tests).</summary>
    public static string LocalRoot => Environment.GetEnvironmentVariable("WINKNIFE_LOCALDATA") is { Length: > 0 } o ? o : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinKnife");

    public static List<AgentLogRoot> All(ClaudeAccountProfile? profile = null)
    {
        profile ??= new ClaudeAccountProfile();
        var home = Home;
        var roots = new List<AgentLogRoot>();
        foreach (var p in profile.LogFolders(home))
        {
            roots.Add(new AgentLogRoot(AgentProvider.Claude, p));
        }

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var codex = !string.IsNullOrWhiteSpace(codexHome) ? codexHome : System.IO.Path.Combine(home, ".codex");
        roots.Add(new AgentLogRoot(AgentProvider.Codex, System.IO.Path.Combine(codex, "sessions")));
        roots.Add(new AgentLogRoot(AgentProvider.Codex, System.IO.Path.Combine(codex, "archived_sessions")));

        var open = new List<string> { System.IO.Path.Combine(home, ".local", "share", "opencode") };
        if (Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg)
        {
            open.Add(System.IO.Path.Combine(xdg, "opencode"));
        }

        open.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode"));
        foreach (var o in open.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(new AgentLogRoot(AgentProvider.OpenCode, o));
        }

        roots.Add(new AgentLogRoot(AgentProvider.Copilot, System.IO.Path.Combine(home, ".copilot", "session-state")));
        return roots;
    }

    /// <summary>Discovery and live file events admit the same provider-specific logs.</summary>
    public bool Accepts(string path)
    {
        var prefix = Path.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Provider == AgentProvider.OpenCode)
        {
            return string.Equals(path, System.IO.Path.Combine(Path, OpenCodeDatabase.FileName), StringComparison.OrdinalIgnoreCase);
        }

        if (!path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Provider != AgentProvider.Copilot)
        {
            return true;
        }

        var parts = path[prefix.Length..].Split('\\', '/');
        return parts.Length == 2 && parts[0].Length > 0 && !parts[0].StartsWith('.') && parts[1].Equals("events.jsonl", StringComparison.OrdinalIgnoreCase);
    }
}

internal delegate bool AgentLineFilter(ReadOnlySpan<byte> line);

internal static class AgentLogReader
{
    public const int ChunkSize = 4 << 20;

    /// <summary>A line longer than this is a pasted file or a tool's output, never a usage record.</summary>
    public const int MaximumLine = 32 << 20;

    public static bool IsLog(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.Equals(OpenCodeDatabase.FileName, StringComparison.OrdinalIgnoreCase)
               || name.Equals(OpenCodeDatabase.FileName + "-wal", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// FNV-1a of the log's first and last few kilobytes before <paramref name="offset"/>, and of the offset itself. A log rewritten
    /// with a different start, or different lines just before where reading stopped, no longer matches.
    /// </summary>
    public static ulong? Fingerprint(SafeFileHandle handle, ulong offset)
    {
        const ulong span = 4096;
        ulong hash = 0xCBF29CE484222325;
        void Mix(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                hash = (hash ^ b) * 0x100000001B3;
            }
        }

        Span<byte> off = stackalloc byte[8];
        BitConverter.TryWriteBytes(off, offset);
        Mix(off);
        var buffer = new byte[span];
        try
        {
            foreach (var start in new[] { 0UL, offset - Math.Min(span, offset) })
            {
                var length = (int)Math.Min(span, offset - start);
                if (length == 0)
                {
                    continue;
                }

                if (RandomAccess.Read(handle, buffer.AsSpan(0, length), (long)start) != length)
                {
                    return null;
                }

                Mix(buffer.AsSpan(0, length));
            }
        }
        catch (IOException)
        {
            return null;
        }

        return hash;
    }

    /// <summary>Log files changed since <paramref name="horizon"/>, oldest last-modified first; Claude subagent logs after every session.</summary>
    public static List<(string Path, AgentProvider Provider)> Discover(IEnumerable<AgentLogRoot> roots, DateTime horizon)
    {
        var found = new List<(string Path, AgentProvider Provider, DateTime Modified, bool Subagent)>();
        foreach (var root in roots)
        {
            if (!root.Exists)
            {
                continue;
            }

            try
            {
                switch (root.Provider)
                {
                    case AgentProvider.OpenCode:
                    {
                        var db = System.IO.Path.Combine(root.Path, OpenCodeDatabase.FileName);
                        if (OpenCodeDatabase.Modified(db) is { } m && m >= horizon)
                        {
                            found.Add((db, root.Provider, m, false));
                        }

                        break;
                    }

                    case AgentProvider.Copilot:
                        // A session can contain a complete workspace checkout; its log has one fixed shallow path.
                        foreach (var dir in new DirectoryInfo(root.Path).EnumerateDirectories())
                        {
                            if (dir.Name.StartsWith('.'))
                            {
                                continue;
                            }

                            var file = new FileInfo(System.IO.Path.Combine(dir.FullName, "events.jsonl"));
                            if (file.Exists && file.LastWriteTimeUtc >= horizon && root.Accepts(file.FullName))
                            {
                                found.Add((file.FullName, root.Provider, file.LastWriteTimeUtc, false));
                            }
                        }

                        break;
                    default:
                        foreach (var file in new DirectoryInfo(root.Path).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                        {
                            if (file.LastWriteTimeUtc >= horizon && root.Accepts(file.FullName)
                                && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                found.Add((file.FullName, root.Provider, file.LastWriteTimeUtc, root.Provider == AgentProvider.Claude && AgentLogCursor.ParentOf(file.FullName) is not null));
                            }
                        }

                        break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that vanished or cannot be listed has no logs.
            }
        }

        return found.OrderBy(f => f.Subagent).ThenBy(f => f.Modified).Select(f => (f.Path, f.Provider)).ToList();
    }

    /// <summary>
    /// Reads what was appended since the last call and hands over each complete line. A replaced or truncated file starts over.
    /// A database has no files to leave out, so its first read starts at <paramref name="horizon"/>.
    /// </summary>
    public static void ReadAppended(AgentLogCursor cursor, DateTime horizon, Func<bool> shouldContinue, AgentLineFilter? including, Action<ReadOnlyMemory<byte>> line)
    {
        if (!shouldContinue())
        {
            return;
        }

        if (cursor.Provider == AgentProvider.OpenCode)
        {
            OpenCodeDatabase.ReadAppended(cursor, horizon, shouldContinue, line);
            return;
        }

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(cursor.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        using (handle)
        {
            var size = (ulong)Math.Max(0, RandomAccess.GetLength(handle));
            var identity = AgentNative.FileIdentity(handle);
            cursor.Modified = File.GetLastWriteTimeUtc(cursor.Path);
            if (identity != cursor.Identity || size < cursor.Offset || (size > cursor.Offset && !cursor.HoldsWhatWasRead(handle)))
            {
                cursor.StartOver(identity);
            }

            try
            {
                while (cursor.Offset < size && shouldContinue())
                {
                    var wanted = (int)Math.Min(ChunkSize, size - cursor.Offset);
                    var pendingLength = cursor.Pending.Length;
                    var buffer = ArrayPool<byte>.Shared.Rent(pendingLength + wanted);
                    try
                    {
                        cursor.Pending.CopyTo(buffer, 0);
                        var read = 0;
                        while (read < wanted)
                        {
                            var n = RandomAccess.Read(handle, buffer.AsSpan(pendingLength + read, wanted - read), (long)cursor.Offset + read);
                            if (n <= 0)
                            {
                                break;
                            }

                            read += n;
                        }

                        if (read == 0)
                        {
                            break;
                        }

                        cursor.Offset += (ulong)read;
                        Split(buffer, pendingLength + read, pendingLength, cursor, including, line);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            catch (IOException)
            {
                // The file vanished mid-read; the next sweep sees it gone.
            }

            cursor.FingerprintRead(handle);
        }
    }

    /// <summary>Streams every structural event of a Copilot log so responses keep their original date and model.</summary>
    public static void ReadCopilotHistory(AgentLogCursor cursor, Func<bool> shouldContinue, Action<ReadOnlyMemory<byte>> line) =>
        ReadAppended(cursor, DateTime.MinValue, shouldContinue, new AgentLineFilter(CopilotReader.HistoryLine), line);

    private static void Split(byte[] buffer, int count, int scanFrom, AgentLogCursor cursor, AgentLineFilter? including, Action<ReadOnlyMemory<byte>> line)
    {
        var start = 0;
        var position = scanFrom;
        while (position < count)
        {
            var found = buffer.AsSpan(position, count - position).IndexOf((byte)'\n');
            if (found < 0)
            {
                break;
            }

            var end = position + found;
            if (cursor.Discarding)
            {
                cursor.Discarding = false;
            }
            else
            {
                var length = end - start;
                if (length > 0 && buffer[end - 1] == '\r')
                {
                    length--;
                }

                if (length > 0 && length <= MaximumLine && (including is null || including(buffer.AsSpan(start, length))))
                {
                    line(buffer.AsMemory(start, length));
                }
            }

            start = end + 1;
            position = start;
        }

        // Once a line is oversized, scan only for its terminator.
        if (cursor.Discarding || count - start > MaximumLine)
        {
            cursor.Pending = [];
            cursor.Discarding = true;
        }
        else
        {
            cursor.Pending = start < count ? buffer.AsSpan(start, count - start).ToArray() : [];
        }
    }
}
