// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Agents;

/// <summary>Everything read so far, one record per response. Not thread-safe: the usage service confines it to its own thread.</summary>
internal sealed class AgentUsageStore
{
    /// <summary>A turn that ended longer ago than this is history found late, not news.</summary>
    public static readonly TimeSpan LateEnd = TimeSpan.FromMinutes(5);

    /// <summary>How long the PC stays offline, awake, before Claude's turns end.</summary>
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(20);

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private List<UsageRecord> _records = [];
    private List<AgentBillable> _billables = [];
    private List<List<string>> _sources = [];
    private Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _replies = new(PathComparer);
    private readonly HashSet<string> _registered = new(PathComparer);
    private readonly Dictionary<string, DateTime> _settled = new(PathComparer);

    public IReadOnlyList<UsageRecord> Records => _records;

    public Dictionary<AgentProvider, AgentLimits> Limits { get; private set; } = [];

    public string? CodexPlan { get; private set; }

    private DateTime _codexPlanObserved = DateTime.MinValue;

    /// <summary>Open turns by log file.</summary>
    public Dictionary<string, LiveSession> Turns { get; private set; } = new(PathComparer);

    /// <summary>Turns gone quiet, by log file: not shown as working, but work that resumes goes on with them.</summary>
    public Dictionary<string, LiveSession> Waiting { get; private set; } = new(PathComparer);

    /// <summary>Off while the logs are first read, so history never replays as news.</summary>
    public bool ReportsTransitions { get; set; }

    /// <summary>A Claude turn waits only an hour for its work to resume; others six.</summary>
    public static TimeSpan ResumeWindow(AgentProvider p) => p == AgentProvider.Claude ? TimeSpan.FromHours(1) : TimeSpan.FromHours(6);

    public List<LiveSession> Live => Turns.Values.Select(t => t.Clone()).ToList();

    public bool ShowsClaudeTurn => Turns.Values.Any(t => t.Provider == AgentProvider.Claude);

    /// <summary>Applies one file's entries and returns the turns they finished.</summary>
    public List<AgentUsageEvent> Apply(List<LogEntry> entries, string file, AgentProvider provider, bool tracksTurns, string? parent, DateTime modified, DateTime now)
    {
        var events = new List<AgentUsageEvent>();
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case LogEntry.Usage u:
                {
                    // Copilot exposes response activity and session-wide token totals, neither of which measures the current root task.
                    var turn = provider == AgentProvider.Copilot ? null : tracksTurns ? file : parent;
                    Add(u.Record, u.Billable, u.Key, file, turn, !tracksTurns);
                    break;
                }

                case LogEntry.UsageModel um:
                {
                    if (!_index.TryGetValue(um.Key, out var position) || _records[position].Model.Length != 0)
                    {
                        break;
                    }

                    var copy = _records[position].Clone();
                    copy.Model = um.Model;
                    _records[position] = copy;
                    break;
                }

                case LogEntry.Limits l:
                    UpdateLimits(l.Reading);
                    break;
                case LogEntry.Plan p:
                    // An archived session read again from its start holds an older plan than the one in use.
                    if (_codexPlanObserved <= p.ObservedAt)
                    {
                        CodexPlan = p.PlanType;
                        _codexPlanObserved = p.ObservedAt;
                    }

                    break;
                case LogEntry.TurnBegan b:
                {
                    if (!tracksTurns)
                    {
                        break;
                    }

                    _settled.Remove(file);
                    // A log rewritten in place is read again from its start; the turn it already holds keeps what its responses added.
                    if ((Turns.TryGetValue(file, out var existing) || Waiting.TryGetValue(file, out existing)) && Math.Abs((existing.Started - b.At).TotalSeconds) < 1)
                    {
                        break;
                    }

                    Waiting.Remove(file);
                    var last = Turns.TryGetValue(file, out var prior) ? prior.LastActivity : b.At;
                    Turns[file] = new LiveSession { Id = file, Provider = provider, Started = b.At, LastActivity = b.At > last ? b.At : last };
                    break;
                }

                case LogEntry.TurnContext c:
                {
                    if (!tracksTurns)
                    {
                        break;
                    }

                    var turn = Turns.GetValueOrDefault(file) ?? Waiting.GetValueOrDefault(file);
                    if (turn is not null)
                    {
                        if (c.Model.Length > 0)
                        {
                            turn.Model = c.Model;
                        }

                        if (c.Project.Length > 0)
                        {
                            turn.Project = c.Project;
                        }
                    }

                    break;
                }

                case LogEntry.TurnActive a:
                {
                    if (!tracksTurns)
                    {
                        break;
                    }

                    _settled.Remove(file);
                    var moment = a.At ?? modified;
                    if (Turns.TryGetValue(file, out var turn))
                    {
                        turn.LastActivity = moment > turn.LastActivity ? moment : turn.LastActivity;
                    }
                    else if (Waiting.Remove(file, out turn))
                    {
                        turn.LastActivity = moment > turn.LastActivity ? moment : turn.LastActivity;
                        Turns[file] = turn;
                    }
                    else
                    {
                        Turns[file] = new LiveSession { Id = file, Provider = provider, Started = moment, LastActivity = moment };
                    }

                    break;
                }

                case LogEntry.TurnSettled s:
                {
                    if (!tracksTurns)
                    {
                        break;
                    }

                    if (!Turns.TryGetValue(file, out var turn) && Waiting.Remove(file, out turn))
                    {
                        Turns[file] = turn;
                    }

                    if (turn is null)
                    {
                        break;
                    }

                    turn.LastActivity = s.At > turn.LastActivity ? s.At : turn.LastActivity;
                    _settled[file] = s.At;
                    break;
                }

                case LogEntry.TurnEnded e:
                {
                    if (!tracksTurns)
                    {
                        break;
                    }

                    _settled.Remove(file);
                    // A turn that went quiet on the way ends as the whole turn.
                    Waiting.Remove(file, out var quiet);
                    Turns.Remove(file, out var turn);
                    turn ??= quiet;
                    if (turn is null || !e.Completed || !ReportsTransitions)
                    {
                        break;
                    }

                    var end = e.At ?? modified;
                    if (now - end > LateEnd)
                    {
                        break;
                    }

                    var duration = e.Duration ?? (end - turn.Started);
                    events.Add(new AgentUsageEvent.Finished(provider, duration < TimeSpan.Zero ? TimeSpan.Zero : duration, turn.Cost, turn.Tokens.Total, turn.Project));
                    break;
                }

                case LogEntry.Reset:
                {
                    var hash = file.IndexOf('#');
                    Forget(hash >= 0 ? file[..hash] : file);
                    break;
                }
            }
        }

        return events;
    }

    /// <summary>A log removed while its turn ran ends that turn without a notice. True when one was showing.</summary>
    public bool Forget(string file)
    {
        Waiting.Remove(file);
        foreach (var key in _settled.Keys.Where(k => PathComparer.Equals(k, file) || k.StartsWith(file + "#", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _settled.Remove(key);
        }

        var removed = Turns.Remove(file);
        foreach (var key in Turns.Keys.Where(k => k.StartsWith(file + "#", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Turns.Remove(key);
            removed = true;
        }

        foreach (var key in Waiting.Keys.Where(k => k.StartsWith(file + "#", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Waiting.Remove(key);
        }

        return removed;
    }

    private void Add(UsageRecord record, AgentBillable billable, string key, string source, string? file, bool subagent)
    {
        var delta = record.Tokens;
        var extra = record.Cost ?? 0;
        if (_index.TryGetValue(key, out var position))
        {
            if (!_sources[position].Contains(source, PathComparer))
            {
                _sources[position].Add(source);
            }

            var old = _records[position];
            var merged = old.Tokens.Merged(record.Tokens);
            if (merged == old.Tokens)
            {
                if (record.Provider == AgentProvider.OpenCode)
                {
                    double? newCost;
                    bool isReported;
                    if (record.ReportedCost)
                    {
                        newCost = record.Cost;
                        isReported = true;
                    }
                    else if (old.ReportedCost)
                    {
                        newCost = old.Cost;
                        isReported = true;
                    }
                    else
                    {
                        newCost = record.Cost ?? old.Cost;
                        isReported = false;
                    }

                    if (newCost != old.Cost || isReported != old.ReportedCost)
                    {
                        var diff = (newCost ?? 0) - (old.Cost ?? 0);
                        old.Cost = newCost;
                        old.ReportedCost = isReported;
                        if (file is not null && (Turns.TryGetValue(file, out var turn) || Waiting.TryGetValue(file, out turn))
                            && record.Date >= turn.Started.AddSeconds(-1))
                        {
                            if (Waiting.Remove(file, out var w))
                            {
                                Turns[file] = w;
                                turn = w;
                            }

                            turn.Cost += diff;
                        }
                    }
                }

                return;
            }

            var combined = _billables[position];
            combined.Tokens = merged;
            combined.LongCacheWrite = Math.Max(combined.LongCacheWrite, billable.LongCacheWrite);
            combined.WebSearches = Math.Max(combined.WebSearches, billable.WebSearches);
            combined.Fast = combined.Fast || billable.Fast;
            combined.Domestic = combined.Domestic || billable.Domestic;
            combined.IsAggregate = combined.IsAggregate || billable.IsAggregate;
            var priced = AgentPricing.Cost(combined, old.Model);
            double? cost;
            bool reported;
            if (record.Provider == AgentProvider.OpenCode)
            {
                if (record.ReportedCost)
                {
                    cost = record.Cost;
                    reported = true;
                }
                else if (old.ReportedCost)
                {
                    cost = old.Cost;
                    reported = true;
                }
                else
                {
                    cost = priced.Cost ?? record.Cost ?? old.Cost;
                    reported = false;
                }
            }
            else
            {
                cost = priced.Cost;
                reported = false;
            }

            delta = merged - old.Tokens;
            extra = (cost ?? 0) - (old.Cost ?? 0);
            _billables[position] = combined;
            old.Tokens = merged;
            old.Cost = cost;
            old.Savings = priced.Savings;
            old.ReportedCost = reported;
        }
        else
        {
            _index[key] = _records.Count;
            _records.Add(record);
            _billables.Add(billable);
            _sources.Add([source]);
        }

        if (file is null)
        {
            return;
        }

        // Noted before the turn check, which skips a reply dated before its turn started: the model answered all the same.
        _replies[file] = _replies.TryGetValue(file, out var prev) && prev > record.Date ? prev : record.Date;
        if (!(Turns.TryGetValue(file, out var t) || Waiting.TryGetValue(file, out t)) || record.Date < t.Started.AddSeconds(-1))
        {
            return;
        }

        if (Waiting.Remove(file, out var moved))
        {
            Turns[file] = moved;
            t = moved;
        }

        t.Tokens += delta;
        t.Cost += extra;
        t.LastActivity = record.Date > t.LastActivity ? record.Date : t.LastActivity;
        if (!subagent)
        {
            if (record.Model.Length > 0)
            {
                t.Model = record.Model;
            }

            if (record.Project.Length > 0)
            {
                t.Project = record.Project;
            }
        }
    }

    /// <summary>Prices every response again after a newer list arrives; reported provider charges stay intact.</summary>
    public void Reprice()
    {
        for (var i = 0; i < _records.Count; i++)
        {
            var r = _records[i];
            if (r.ReportedCost)
            {
                continue;
            }

            var priced = AgentPricing.Cost(_billables[i], r.Model);
            var recordedZero = r.Provider == AgentProvider.OpenCode && r.Cost == 0;
            r.Cost = priced.Cost ?? (recordedZero ? 0 : null);
            r.Savings = priced.Savings;
        }
    }

    public void SetLimits(AgentLimits reading) => Limits[reading.Provider] = reading;

    /// <summary>Keeps the newer of two readings of an account.</summary>
    public void UpdateLimits(AgentLimits reading)
    {
        if (!Limits.TryGetValue(reading.Provider, out var cur) || cur.ObservedAt <= reading.ObservedAt)
        {
            Limits[reading.Provider] = reading;
        }
    }

    public void ClearLimits(AgentProvider provider) => Limits.Remove(provider);

    /// <summary>A turn that has written nothing for this long is not being worked on; it waits aside for a while.</summary>
    public void CloseIdleTurns(DateTime now, TimeSpan idle)
    {
        foreach (var (file, turn) in Turns.ToList())
        {
            if (now - turn.LastActivity >= idle)
            {
                Turns.Remove(file);
                Waiting[file] = turn;
            }
        }

        foreach (var (file, turn) in Waiting.ToList())
        {
            if (now - turn.LastActivity >= ResumeWindow(turn.Provider))
            {
                Waiting.Remove(file);
            }
        }

        foreach (var k in _replies.Keys.ToList())
        {
            if (!Turns.ContainsKey(k) && !Waiting.ContainsKey(k))
            {
                _replies.Remove(k);
            }
        }
    }

    /// <summary>A turn whose last step ended expecting more, with nothing after it for a while, ends silently. True when one was showing.</summary>
    public bool CloseSettledTurns(DateTime now)
    {
        var removed = false;
        foreach (var (file, date) in _settled.ToList())
        {
            if (now - date >= AgentLogParser.OpenCodeSettle)
            {
                _settled.Remove(file);
                Waiting.Remove(file);
                if (Turns.Remove(file))
                {
                    removed = true;
                }
            }
        }

        return removed;
    }

    /// <summary>A Claude session whose process is gone writes nothing that ends the turn; its process record says so sooner.</summary>
    public bool CloseEndedTurns(AgentSessionRegistry processes, bool atLaunch = false)
    {
        _registered.IntersectWith(Turns.Keys);
        var closed = false;
        foreach (var (file, turn) in Turns.ToList())
        {
            if (turn.Provider != AgentProvider.Claude)
            {
                continue;
            }

            var session = Path.GetFileNameWithoutExtension(file);
            if (processes.Running.Contains(session))
            {
                _registered.Add(file);
            }
            else if (processes.Ended.Contains(session) || (processes.Complete && (_registered.Contains(file) || (atLaunch && processes.Listed))))
            {
                _registered.Remove(file);
                closed = Forget(file) || closed;
            }
        }

        return closed;
    }

    /// <summary>Claude retries without a word while offline, then gives up; its turns end once the PC has been offline for the grace period.</summary>
    public bool CloseOfflineTurns(DateTime offline, TimeSpan elapsed, Func<HashSet<string>> running)
    {
        if (elapsed < OfflineGrace)
        {
            return false;
        }

        var commands = running();

        bool Ends(KeyValuePair<string, LiveSession> e) =>
            e.Value.Provider == AgentProvider.Claude && !commands.Contains(e.Key) && (_replies.TryGetValue(e.Key, out var r) ? r : DateTime.MinValue) < offline;

        var closed = Turns.Any(Ends);
        foreach (var e in Turns.Where(Ends).ToList())
        {
            Turns.Remove(e.Key);
        }

        foreach (var e in Waiting.Where(Ends).ToList())
        {
            Waiting.Remove(e.Key);
        }

        foreach (var k in _settled.Keys.ToList())
        {
            if (!Turns.ContainsKey(k) && !Waiting.ContainsKey(k))
            {
                _settled.Remove(k);
            }
        }

        return closed;
    }

    /// <summary>Keeps memory bounded to the history the island can show.</summary>
    public void DropRecords(DateTime before)
    {
        if (_records.Any(r => r.Date < before))
        {
            KeepRecords(i => _records[i].Date >= before);
        }
    }

    /// <summary>Takes back what logs gone or rewritten since they were read gave; their turns end without a notice.</summary>
    public void Forget(HashSet<string> files)
    {
        foreach (var f in files)
        {
            Forget(f);
        }

        var emptied = false;
        foreach (var s in _sources)
        {
            if (s.Any(files.Contains))
            {
                s.RemoveAll(files.Contains);
                emptied |= s.Count == 0;
            }
        }

        if (emptied)
        {
            KeepRecords(i => _sources[i].Count > 0);
        }
    }

    /// <summary>The logs holding a response that one of <paramref name="files"/> also holds.</summary>
    public HashSet<string> FilesSharingWith(HashSet<string> files)
    {
        var sharing = new HashSet<string>(PathComparer);
        foreach (var list in _sources)
        {
            if (list.Any(files.Contains))
            {
                sharing.UnionWith(list);
            }
        }

        return sharing;
    }

    /// <summary>Every log that gave a response or holds a turn.</summary>
    public HashSet<string> Files
    {
        get
        {
            var set = new HashSet<string>(PathComparer);
            foreach (var s in _sources)
            {
                set.UnionWith(s);
            }

            set.UnionWith(Turns.Keys);
            set.UnionWith(Waiting.Keys);
            return set;
        }
    }

    private void KeepRecords(Func<int, bool> keep)
    {
        var records = new List<UsageRecord>();
        var billables = new List<AgentBillable>();
        var sources = new List<List<string>>();
        var positions = new Dictionary<int, int>();
        for (var i = 0; i < _records.Count; i++)
        {
            if (!keep(i))
            {
                continue;
            }

            positions[i] = records.Count;
            records.Add(_records[i]);
            billables.Add(_billables[i]);
            sources.Add(_sources[i]);
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, pos) in _index)
        {
            if (positions.TryGetValue(pos, out var np))
            {
                index[key] = np;
            }
        }

        _index = index;
        _records = records;
        _billables = billables;
        _sources = sources;
    }

    // ------------------------------------------------------------------------------------------ persistence

    public sealed class SavedRecord
    {
        public string Key { get; set; } = "";

        public UsageRecord Record { get; set; } = new();

        public AgentBillable Billable { get; set; }

        public List<string> Sources { get; set; } = [];
    }

    public sealed class Saved
    {
        public List<SavedRecord> Records { get; set; } = [];

        public List<AgentLimits> Limits { get; set; } = [];

        public string? CodexPlan { get; set; }

        public DateTime CodexPlanObserved { get; set; } = DateTime.MinValue;

        public List<LiveSession> Turns { get; set; } = [];

        public List<LiveSession> Waiting { get; set; } = [];
    }

    /// <summary>What is kept between launches: every counter the logs gave, and none of their text. OpenCode's is never kept.</summary>
    public Saved Save()
    {
        var keys = new string[_records.Count];
        foreach (var (key, pos) in _index)
        {
            keys[pos] = key;
        }

        var saved = new Saved { CodexPlan = CodexPlan, CodexPlanObserved = _codexPlanObserved };
        for (var i = 0; i < _records.Count; i++)
        {
            if (_records[i].Provider == AgentProvider.OpenCode)
            {
                continue;
            }

            saved.Records.Add(new SavedRecord { Key = keys[i], Record = _records[i], Billable = _billables[i], Sources = _sources[i] });
        }

        saved.Limits = Limits.Values.OrderBy(l => l.Provider.Raw(), StringComparer.Ordinal).ToList();
        saved.Turns = Turns.Values.Where(t => t.Provider != AgentProvider.OpenCode).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        saved.Waiting = Waiting.Values.Where(t => t.Provider != AgentProvider.OpenCode).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        return saved;
    }

    public static AgentUsageStore Restore(Saved saved)
    {
        var store = new AgentUsageStore
        {
            _records = saved.Records.Select(r => r.Record).ToList(),
            _billables = saved.Records.Select(r => r.Billable).ToList(),
            _sources = saved.Records.Select(r => r.Sources).ToList(),
            CodexPlan = saved.CodexPlan,
            _codexPlanObserved = saved.CodexPlanObserved,
        };
        for (var i = 0; i < saved.Records.Count; i++)
        {
            store._index[saved.Records[i].Key] = i;
        }

        store.Limits = saved.Limits.GroupBy(l => l.Provider).ToDictionary(g => g.Key, g => g.Last());
        store.Turns = new Dictionary<string, LiveSession>(saved.Turns.ToDictionary(t => t.Id), PathComparer);
        store.Waiting = new Dictionary<string, LiveSession>(saved.Waiting.ToDictionary(t => t.Id), PathComparer);
        return store;
    }
}

/// <summary>The Claude sessions with a process, from <c>sessions\&lt;pid&gt;.json</c> Claude Code keeps beside its logs while it runs.</summary>
internal sealed class AgentSessionRegistry
{
    /// <summary>Session ids with a running process.</summary>
    public HashSet<string> Running { get; } = [];

    /// <summary>Session ids whose recorded process no longer runs.</summary>
    public HashSet<string> Ended { get; } = [];

    /// <summary>False when a record could not be read (it may be mid-write), so a missing session proves nothing.</summary>
    public bool Complete { get; set; } = true;

    /// <summary>A sessions folder could be listed, so this Claude Code keeps records.</summary>
    public bool Listed { get; set; }

    public static AgentSessionRegistry Read(IEnumerable<string> folders, Func<int, bool>? isRunning = null)
    {
        isRunning ??= IsRunning;
        var registry = new AgentSessionRegistry();
        foreach (var folder in folders)
        {
            string[] names;
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                names = Directory.GetFiles(folder, "*.json");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            registry.Listed = true;
            foreach (var path in names)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out _))
                {
                    continue;
                }

                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length >= 1 << 16)
                    {
                        registry.Complete = false;
                        continue;
                    }

                    using var doc = System.Text.Json.JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    if (root.Prop("pid").Num() is not { } pid || root.Str("sessionId") is not { Length: > 0 } session)
                    {
                        registry.Complete = false;
                        continue;
                    }

                    // A session run in a container or virtual machine that shares this folder names a process this PC cannot see.
                    if (root.Str("pidDomain") is { } domain && !domain.StartsWith("win32:", StringComparison.OrdinalIgnoreCase) && domain != "win32")
                    {
                        continue;
                    }

                    if (isRunning((int)Math.Min(pid, int.MaxValue)))
                    {
                        registry.Running.Add(session);
                    }
                    else
                    {
                        registry.Ended.Add(session);
                    }
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    registry.Complete = false;
                }
            }
        }

        // A session resumed by a new process after an old one was killed.
        registry.Ended.ExceptWith(registry.Running);
        return registry;
    }

    public static bool IsRunning(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        var handle = AgentNative.OpenProcess(AgentNative.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            // Access denied still means the process exists.
            return System.Runtime.InteropServices.Marshal.GetLastWin32Error() == 5;
        }

        try
        {
            return AgentNative.GetExitCodeProcess(handle, out var code) && code == AgentNative.StillActive;
        }
        finally
        {
            AgentNative.CloseHandle(handle);
        }
    }
}
