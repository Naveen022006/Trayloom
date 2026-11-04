// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;

namespace Trayloom.Core.Native;

/// <summary>One foreign window Trayloom has put into a state the person cannot see (hidden, transparent, parked far off-screen, being given back).</summary>
/// <param name="Hwnd">The window handle.</param>
/// <param name="Pid">The window's process.</param>
/// <param name="Process">The process's executable name without extension (guards against a reused pid).</param>
/// <param name="Class">The window class (guards against a reused handle).</param>
/// <param name="Kind">One of <see cref="WindowGuardianState.Kept"/>, <see cref="WindowGuardianState.Ghost"/>, <see cref="WindowGuardianState.Parked"/>, <see cref="WindowGuardianState.Releasing"/>.</param>
/// <param name="ExStyle">For a ghost: the extended style the window had before.</param>
/// <param name="Home">Where the person had the window (left, top, right, bottom in screen coordinates).</param>
public sealed record TrackedWindow(long Hwnd, uint Pid, string Process, string Class, string Kind, long? ExStyle, int[]? Home);

/// <summary>What the state file holds: the owning Trayloom process and the windows it owes the person.</summary>
public sealed record GuardianSnapshot(uint OwnerPid, long OwnerStart, string Updated, IReadOnlyList<TrackedWindow> Windows);

/// <summary>
/// The crash-proof record of every foreign window Trayloom has made invisible (<c>window-guardian.json</c>, written synchronously on
/// every change, not through the debounced settings store). The guardian process and the next start of Trayloom read it to give the
/// windows back. A record is removed only once the window is verified visible again.
/// </summary>
public static class WindowGuardianState
{
    /// <summary>Hidden (SW_HIDE) so that it has no taskbar button; the island keeps playing through it.</summary>
    public const string Kept = "kept";

    /// <summary>Transparent and click-through (the style it had before is recorded).</summary>
    public const string Ghost = "ghost";

    /// <summary>Visible but moved far off-screen.</summary>
    public const string Parked = "parked";

    /// <summary>Being given back to the person; the show has not been verified yet.</summary>
    public const string Releasing = "releasing";

    private static readonly object Gate = new();
    private static readonly List<TrackedWindow> Tracked = [];
    private static FileLog? _log;
    private static bool _warned;
    private static bool _moveWarned;
    private static uint _ownerPid;
    private static long _ownerStart;

    /// <summary>The state file (under <see cref="AppPaths.Root"/>, so TRAYLOOM_DATA redirects it).</summary>
    public static string FilePath => Path.Combine(AppPaths.Root, "window-guardian.json");

    /// <summary>The event the owner sets after every write (the guardian waits on it).</summary>
    public static string ChangedEventName(uint ownerPid) => $@"Local\Trayloom.WindowGuardian.Changed.{ownerPid.ToString(CultureInfo.InvariantCulture)}";

    internal static FileLog Log => _log ??= new FileLog(Path.Combine(AppPaths.LogDirectory, "window-guardian.log"));

    // ---------------------------------------------------------------- owner side

    /// <summary>Records (or replaces) a window; written to disk before this returns.</summary>
    public static void Track(nint hwnd, uint pid, string process, string cls, string kind, long? exStyle = null, int[]? home = null)
    {
        var record = new TrackedWindow(hwnd, pid, process, cls, kind, exStyle, home);
        var wasEmpty = false;
        lock (Gate)
        {
            var index = Tracked.FindIndex(t => t.Hwnd == hwnd && t.Kind == kind);
            if (index >= 0)
            {
                if (SameRecord(Tracked[index], record))
                {
                    return;
                }

                Tracked[index] = record;
            }
            else
            {
                wasEmpty = Tracked.Count == 0;
                Tracked.Add(record);
            }

            Write();
        }

        if (wasEmpty)
        {
            WindowGuardianClient.Ensure();
        }
    }

    /// <summary>Forgets one record (a window that is in one state no more).</summary>
    public static void Untrack(nint hwnd, string kind)
    {
        lock (Gate)
        {
            if (Tracked.RemoveAll(t => t.Hwnd == hwnd && t.Kind == kind) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>Forgets every record of the window (it is gone, or fully given back).</summary>
    public static void UntrackWindow(nint hwnd)
    {
        lock (Gate)
        {
            if (Tracked.RemoveAll(t => t.Hwnd == hwnd) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>The owner's current records.</summary>
    public static GuardianSnapshot Snapshot()
    {
        lock (Gate)
        {
            return new GuardianSnapshot(OwnerPid, OwnerStart, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), [.. Tracked]);
        }
    }

    /// <summary>Whether the owner tracks any window.</summary>
    public static bool HasTracked
    {
        get
        {
            lock (Gate)
            {
                return Tracked.Count > 0;
            }
        }
    }

    /// <summary>Whether a window of this process is tracked (the owner must not treat such a process as "has no window").</summary>
    public static bool IsPidTracked(uint pid)
    {
        lock (Gate)
        {
            return Tracked.Exists(t => t.Pid == pid);
        }
    }

    /// <summary>The records of this process's window of this kind, if any.</summary>
    public static bool IsTracked(nint hwnd, string kind)
    {
        lock (Gate)
        {
            return Tracked.Exists(t => t.Hwnd == hwnd && t.Kind == kind);
        }
    }

    internal static uint OwnerPid => _ownerPid != 0 ? _ownerPid : (_ownerPid = (uint)Environment.ProcessId);

    internal static long OwnerStart => _ownerStart != 0 ? _ownerStart : (_ownerStart = GuardianNative.OwnStartTime());

    private static bool SameRecord(TrackedWindow a, TrackedWindow b) =>
        a.Pid == b.Pid && a.Process == b.Process && a.Class == b.Class && a.ExStyle == b.ExStyle && (a.Home ?? []).AsSpan().SequenceEqual(b.Home ?? []);

    /// <summary>Writes the owner's records (called with the lock held) and wakes the guardian.</summary>
    private static void Write()
    {
        var snapshot = new GuardianSnapshot(OwnerPid, OwnerStart, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), [.. Tracked]);
        WriteFile(snapshot);
        try
        {
            using var changed = new EventWaitHandle(false, EventResetMode.AutoReset, ChangedEventName(OwnerPid));
            changed.Set();
        }
        catch
        {
            // the guardian then finds out on its next wake-up
        }
    }

    // ---------------------------------------------------------------- the file

    /// <summary>
    /// Holds the cross-process lock of the state file (<c>Local\Trayloom.WindowGuardian.State</c>) for the length of a read-check-write. The
    /// owner and the guardian of a dead owner both go through it, so a guardian cannot overwrite or delete the file of the Trayloom that
    /// has taken over meanwhile. Held for file I/O only, never across a call into another process's window.
    /// </summary>
    private sealed class FileLock : IDisposable
    {
        private Mutex? _mutex;

        public FileLock()
        {
            try
            {
                _mutex = new Mutex(false, @"Local\Trayloom.WindowGuardian.State");
                try
                {
                    if (!_mutex.WaitOne(2000))
                    {
                        // a stuck holder must not stop the owner from recording a window; the write is atomic either way
                        _mutex.Dispose();
                        _mutex = null;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // the holder died inside; the lock is ours
                }
            }
            catch
            {
                _mutex?.Dispose();
                _mutex = null;
            }
        }

        public void Dispose()
        {
            if (_mutex is null)
            {
                return;
            }

            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // not owned
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }

    /// <summary>Writes a snapshot atomically (temp file + move); a failure is logged once and swallowed.</summary>
    internal static void WriteFile(GuardianSnapshot snapshot)
    {
        using var fileLock = new FileLock();
        WriteFileCore(snapshot);
    }

    /// <summary>
    /// Writes a snapshot only while the file does not belong to another owner (or does not exist): the guardian of a dead owner uses this for
    /// its last write. False = a newer owner has taken the file over and nothing was written.
    /// </summary>
    internal static bool WriteFileIfOwner(uint ownerPid, GuardianSnapshot snapshot)
    {
        using var fileLock = new FileLock();
        var current = Read(out _);
        if (current is not null && current.OwnerPid != ownerPid)
        {
            return false;
        }

        WriteFileCore(snapshot);
        return true;
    }

    private static void WriteFileCore(GuardianSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var path = FilePath;
            var temp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllBytes(temp, Serialize(snapshot));
            Replace(temp, path);
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn("Could not write " + FilePath + ": " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Puts the temp file in place of the state file. A reader that has the file open (or antivirus) can make the move fail for a moment,
    /// so it is retried a few times, then <see cref="File.Replace(string, string, string?)"/> and finally a write in place are tried.
    /// </summary>
    private static void Replace(string temp, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (!_moveWarned)
                {
                    _moveWarned = true;
                    Log.Warn("Replacing " + path + " failed (" + e.GetType().Name + "); trying File.Replace, then a write in place");
                }

                break;
            }
        }

        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
                return;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // fall through to the write in place
        }

        File.WriteAllBytes(path, File.ReadAllBytes(temp));
        try
        {
            File.Delete(temp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // CleanTemporaryFiles removes it on the next start
        }
    }

    /// <summary>Removes <c>window-guardian.json.&lt;pid&gt;.tmp</c> files an earlier run left behind (a write that was cut short).</summary>
    internal static void CleanTemporaryFiles()
    {
        try
        {
            var own = "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "window-guardian.json.*.tmp"))
            {
                try
                {
                    if (!file.EndsWith(own, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromMinutes(1))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // someone else holds it; the next start tries again
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // no folder yet, or no access: nothing to clean
        }
    }

    /// <summary>Deletes the state file when it still belongs to <paramref name="ownerPid"/> (a newer owner's file is left alone).</summary>
    internal static void DeleteIfOwner(uint ownerPid)
    {
        try
        {
            using var fileLock = new FileLock();
            var current = Read(out var unreadable);
            if ((current is not null && current.OwnerPid == ownerPid) || unreadable)
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Reads the file. Null with <paramref name="unreadable"/> false: there is none. Null with it true: it exists but cannot be understood.
    /// </summary>
    internal static GuardianSnapshot? Read(out bool unreadable)
    {
        unreadable = false;
        byte[] bytes;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                // Shared with a writer's move: the default share mode of ReadAllBytes would make the owner's replace fail.
                using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    bytes = copy.ToArray();
                }

                break;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < 5)
            {
                // the owner is replacing it right now
                Thread.Sleep(20);
            }
            catch
            {
                unreadable = true;
                return null;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            var windows = new List<TrackedWindow>();
            if (root.TryGetProperty("windows", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    int[]? home = null;
                    if (item.TryGetProperty("home", out var h) && h.ValueKind == JsonValueKind.Array && h.GetArrayLength() == 4)
                    {
                        home = [.. h.EnumerateArray().Select(x => x.GetInt32())];
                    }

                    long? ex = item.TryGetProperty("exStyle", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : null;
                    windows.Add(new TrackedWindow(
                        item.GetProperty("hwnd").GetInt64(),
                        item.GetProperty("pid").GetUInt32(),
                        item.GetProperty("process").GetString() ?? "",
                        item.GetProperty("cls").GetString() ?? "",
                        item.GetProperty("kind").GetString() ?? "",
                        ex,
                        home));
                }
            }

            return new GuardianSnapshot(
                root.GetProperty("ownerPid").GetUInt32(),
                root.GetProperty("ownerStart").GetInt64(),
                root.TryGetProperty("updated", out var u) ? u.GetString() ?? "" : "",
                windows);
        }
        catch
        {
            unreadable = true;
            return null;
        }
    }

    private static byte[] Serialize(GuardianSnapshot s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteNumber("ownerPid", s.OwnerPid);
            w.WriteNumber("ownerStart", s.OwnerStart);
            w.WriteString("updated", s.Updated);
            w.WriteStartArray("windows");
            foreach (var t in s.Windows)
            {
                w.WriteStartObject();
                w.WriteNumber("hwnd", t.Hwnd);
                w.WriteNumber("pid", t.Pid);
                w.WriteString("process", t.Process);
                w.WriteString("cls", t.Class);
                w.WriteString("kind", t.Kind);
                if (t.ExStyle is { } ex)
                {
                    w.WriteNumber("exStyle", ex);
                }
                else
                {
                    w.WriteNull("exStyle");
                }

                if (t.Home is { Length: 4 } home)
                {
                    w.WriteStartArray("home");
                    foreach (var n in home)
                    {
                        w.WriteNumberValue(n);
                    }

                    w.WriteEndArray();
                }
                else
                {
                    w.WriteNull("home");
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    // ---------------------------------------------------------------- next start

    /// <summary>
    /// The records a previous run left when it died (null when there are none, or when their owner still runs). Read synchronously, before
    /// anything of this run can write: the first write replaces the file.
    /// </summary>
    public static GuardianSnapshot? TakeStale()
    {
        try
        {
            CleanTemporaryFiles();
            var snapshot = Read(out _);
            if (snapshot is null || snapshot.Windows.Count == 0 || snapshot.OwnerPid == OwnerPid)
            {
                return null;
            }

            return GuardianNative.IsProcessAlive(snapshot.OwnerPid, snapshot.OwnerStart) ? null : snapshot;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The rescue of a stale snapshot is over: the records that are still unresolved stay in the file (the next start tries again), the
    /// rest is gone. Does nothing when a newer owner has already replaced the file.
    /// </summary>
    public static void ReleaseStale(GuardianSnapshot stale, IReadOnlyList<TrackedWindow> unresolved)
    {
        try
        {
            lock (Gate)
            {
                // read-check-write under the file lock: the guardian of the dead owner and a newer owner's own writes go through it too
                using var fileLock = new FileLock();
                var current = Read(out _);
                if (current is null || current.OwnerPid != stale.OwnerPid)
                {
                    return;
                }

                if (unresolved.Count == 0)
                {
                    File.Delete(FilePath);
                }
                else
                {
                    WriteFileCore(stale with { Windows = unresolved });
                }
            }
        }
        catch
        {
            // best effort
        }
    }
}
