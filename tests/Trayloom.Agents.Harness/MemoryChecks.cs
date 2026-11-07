// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text.Json;
using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

internal static partial class Program
{
    /// <summary>
    /// The usage store's memory: a restored archive shares one string per model, project, session and log path instead of one per
    /// response, and the saved JSON leaves out the token totals it can compute. With TRAYLOOM_AGENT_ARCHIVE pointing at a COPY of
    /// agent-usage.bin, also reports how much memory and time the real archive costs (nothing is written next to that copy).
    /// </summary>
    private static void Memory()
    {
        var saved = new AgentUsageStore.Saved();
        for (var i = 0; i < 200; i++)
        {
            var record = new UsageRecord
            {
                Provider = AgentProvider.Claude,
                Date = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                Model = "claude-opus-5",
                Project = "Trayloom",
                Session = "de4739b0-088d-49bc-a6f8-cfcb40ff34b1",
                Tokens = new AgentTokens(2, 100, 200, 30 + i, 4),
            };
            saved.Records.Add(new AgentUsageStore.SavedRecord
            {
                Key = "claude:msg_" + i,
                Record = record,
                Billable = new AgentBillable { Tokens = record.Tokens },
                Sources = [@"C:\Users\someone\.claude\projects\E--Trayloom\de4739b0-088d-49bc-a6f8-cfcb40ff34b1.jsonl"],
            });
        }

        // A JSON round trip gives every record its own string instances, as reading the archive does.
        var json = JsonSerializer.Serialize(saved);
        Check(!json.Contains("\"Total\"", StringComparison.Ordinal) && !json.Contains("\"CacheHitRate\"", StringComparison.Ordinal),
            "archive: computed token totals are not written");
        var copy = JsonSerializer.Deserialize<AgentUsageStore.Saved>(json)!;
        Check(!ReferenceEquals(copy.Records[0].Record.Session, copy.Records[1].Record.Session), "archive: a read gives each record its own strings");
        var store = AgentUsageStore.Restore(copy);
        var records = store.Records;
        Check(records.Count == 200 && records[199].Tokens.Output == 229 && records[199].Tokens.Total == 2 + 100 + 200 + 229,
            "archive: restored counts are intact");
        Check(ReferenceEquals(records[0].Session, records[199].Session) && ReferenceEquals(records[0].Model, records[199].Model)
              && ReferenceEquals(records[0].Project, records[199].Project), "archive: restored records share their strings");
        Check(store.Files.Count == 1, "archive: the one log is still known as the source");

        // The snapshot places responses by UTC day boundaries now: each lands in the local day and hour ToLocalTime gives it.
        var now = DateTime.UtcNow;
        var spread = new List<UsageRecord>();
        for (var minutes = 0; minutes < 4 * 24 * 60; minutes += 37)
        {
            spread.Add(new UsageRecord { Provider = AgentProvider.Claude, Date = now.AddMinutes(-minutes), Model = "claude-opus-5", Requests = 1 });
        }

        var snapshot = AgentUsageSummary.Snapshot(spread, [], [], [], [AgentProvider.Claude], now);
        var placed = true;
        foreach (var bucket in snapshot.Days.Where(b => b.Start >= now.ToLocalTime().Date.AddDays(-3)))
        {
            var expected = spread.Count(r => r.Date.ToLocalTime() >= bucket.Start && r.Date.ToLocalTime() < bucket.Start.AddDays(1));
            placed &= bucket.Total.Requests == expected;
        }

        foreach (var bucket in snapshot.Hours)
        {
            var expected = spread.Count(r => r.Date.ToLocalTime() >= bucket.Start && r.Date.ToLocalTime() < bucket.Start.AddHours(1));
            placed &= bucket.Total.Requests == expected;
        }

        Check(placed, "snapshot: responses land in the same local days and hours as before");

        if (Environment.GetEnvironmentVariable("TRAYLOOM_AGENT_ARCHIVE") is not { Length: > 0 } path || !File.Exists(path))
        {
            Console.WriteLine("(TRAYLOOM_AGENT_ARCHIVE not set: pass a COPY of agent-usage.bin to measure the real archive)");
            return;
        }

        var baseline = Settled();
        var watch = Stopwatch.StartNew();
        var (real, cursors) = LoadArchive(path);
        var loadMs = watch.ElapsedMilliseconds;
        var held = Settled();
        Console.WriteLine($"real archive: {real.Records.Count} records, {cursors.Count} cursors, load {loadMs} ms");
        watch.Restart();
        var bytes = 0L;
        using (var counter = new CountingStream())
        {
            using (var zip = new System.IO.Compression.GZipStream(counter, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                JsonSerializer.Serialize(zip, new AgentUsageArchive.Contents { Store = real.Save(), Cursors = cursors });
            }

            bytes = counter.Length;
        }

        Console.WriteLine($"real archive: save {watch.ElapsedMilliseconds} ms, {bytes >> 20} MB compressed");

        // The usage service builds one snapshot per change it publishes (every couple of seconds while an agent works).
        var providers = new HashSet<AgentProvider>(Enum.GetValues<AgentProvider>());
        AgentUsageSummary.Snapshot(real.Records, real.Limits, [], [], providers, DateTime.UtcNow);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        for (var i = 0; i < 5; i++)
        {
            AgentUsageSummary.Snapshot(real.Records, real.Limits, [], [], providers, DateTime.UtcNow);
        }

        Console.WriteLine($"real archive: snapshot {watch.ElapsedMilliseconds / 5} ms, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 5 >> 10} KB allocated each");
        GC.KeepAlive(real);
        GC.KeepAlive(cursors);
        real = null!;
        cursors = null!;
        var dropped = Settled();
        Console.WriteLine($"real archive: the store and cursors hold {(held - dropped) >> 20} MB of heap (heap {baseline >> 20} MB before, {held >> 20} MB loaded)");
    }

    /// <summary>The live heap after full collections: what is still reachable, without garbage or free space.</summary>
    private static long Settled()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    /// <summary>Reads the archive copy as the usage service does at start: restores the store and keeps the saved cursors.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (AgentUsageStore Store, List<AgentLogCursor.SavedCursor> Cursors) LoadArchive(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        var contents = JsonSerializer.Deserialize<AgentUsageArchive.Contents>(gzip)!;
        return (AgentUsageStore.Restore(contents.Store), contents.Cursors);
    }

    /// <summary>Counts the bytes written and keeps none.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _length += count;
    }
}
