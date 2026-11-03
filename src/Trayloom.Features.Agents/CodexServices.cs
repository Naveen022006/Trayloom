// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Agents;

/// <summary>
/// The Codex account's banked resets for the AI page: read when their card shows, at most every few minutes, and used only when the person
/// confirms (an explicit click on "Reset").
/// </summary>
public sealed class CodexResetService
{
    /// <summary>A use of a reset that ended, told on the card for a moment.</summary>
    public sealed record Finished(CodexOutcome? Outcome, DateTime Date);

    /// <summary>A reading this recent still stands; a failed one is tried sooner.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>Long enough for any reply still on its way; after it, a fresh reading tells whether an unanswered use happened.</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(10);

    private sealed record Attempt(string Key, string? Credit, DateTime Date);

    private readonly AgentUsageService _usage;
    private readonly object _gate = new();
    private DateTime _attempted = DateTime.MinValue;
    private string? _shellPath;
    private Attempt? _pending;

    public CodexResetService(AgentUsageService usage) => _usage = usage;

    public CodexResetSummary? Summary { get; private set; }

    public CodexFailure? Failure { get; private set; }

    public bool Checking { get; private set; }

    public bool Redeeming { get; private set; }

    public Finished? LastFinished { get; private set; }

    /// <summary>Raised (any thread) when any of the above changed.</summary>
    public event Action? Changed;

    public void RefreshIfStale(DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        // Only the person can sign Codex in or update it, so those wait as long as a reading.
        bool settled;
        lock (_gate)
        {
            settled = Failure is null or CodexFailure.NeedsSignIn or CodexFailure.Outdated;
        }

        if (t - _attempted >= (settled ? Freshness : RetryAfter))
        {
            Refresh();
        }
    }

    public void Refresh(bool searchingShell = false)
    {
        lock (_gate)
        {
            if (Checking || Redeeming)
            {
                return;
            }

            Checking = true;
            _attempted = DateTime.UtcNow;
        }

        Changed?.Invoke();
        var known = _shellPath;
        _ = Task.Run(() =>
        {
            var path = searchingShell && known is null ? CodexServer.WhereCodexPath() : known;
            (CodexResetSummary? Summary, CodexFailure? Failure) result;
            if (CodexServer.Executable(AgentLogRoot.Home, path) is { } executable)
            {
                result = CodexServer.Check(executable, CodexServer.BuildEnvironment(executable, path));
            }
            else
            {
                result = (null, CodexFailure.Missing);
            }

            lock (_gate)
            {
                _shellPath = path;
                Checking = false;
            }

            if (result.Summary is { } summary)
            {
                Accept(summary);
            }
            else
            {
                Fail(result.Failure ?? CodexFailure.Unreachable);
            }

            Changed?.Invoke();
        });
    }

    /// <summary>Uses the reset that expires first, or tries the use that went unanswered again. Only ever called from the card's confirmed button.</summary>
    public void Redeem()
    {
        Attempt attempt;
        string? path;
        lock (_gate)
        {
            if (Redeeming || Summary is not { Available: > 0 } summary)
            {
                return;
            }

            Redeeming = true;
            var now = DateTime.UtcNow;
            attempt = _pending is { } p && now - p.Date < RetryWindow ? p : new Attempt(Guid.NewGuid().ToString(), summary.Resets.FirstOrDefault()?.Id, now);
            _pending = attempt;
            path = _shellPath;
        }

        Changed?.Invoke();
        _ = Task.Run(() =>
        {
            if (CodexServer.Executable(AgentLogRoot.Home, path) is not { } executable)
            {
                FinishRedeem(null, CodexFailure.Missing, null);
                return;
            }

            var result = CodexServer.Redeem(executable, CodexServer.BuildEnvironment(executable, path), attempt.Credit, attempt.Key);
            FinishRedeem(result.Outcome, result.Failure, result.Summary);
        });
    }

    private void FinishRedeem(CodexOutcome? outcome, CodexFailure? failure, CodexResetSummary? summary)
    {
        lock (_gate)
        {
            Redeeming = false;
            if (failure is null && outcome is { } o)
            {
                _pending = null;
                LastFinished = new Finished(o, DateTime.UtcNow);
            }
            else if (failure is CodexFailure.Unreachable or CodexFailure.Refused or null)
            {
                // No outcome came back. Codex also answers with an error when its own request to the account timed out or its reply was
                // lost, so the use may have happened on the way: it keeps its key.
                LastFinished = new Finished(null, DateTime.UtcNow);
            }
            else
            {
                _pending = null;
            }
        }

        if (failure is { } f && f is not (CodexFailure.Unreachable or CodexFailure.Refused))
        {
            Fail(f);
        }

        if (summary is not null)
        {
            Accept(summary);
        }
        else if (Failure is null or CodexFailure.Unreachable or CodexFailure.Refused)
        {
            Changed?.Invoke();
            Refresh();
            return;
        }

        Changed?.Invoke();
    }

    private void Accept(CodexResetSummary summary)
    {
        lock (_gate)
        {
            Summary = summary;
            Failure = null;
            _attempted = summary.CheckedAt;
        }

        if (summary.Limits is { } limits)
        {
            _usage.NoteLimits(limits);
        }
    }

    private void Fail(CodexFailure failure)
    {
        lock (_gate)
        {
            Failure = failure;
            // A lost or refused reply leaves the last count standing; the rest make it moot.
            if (failure is not (CodexFailure.Unreachable or CodexFailure.Refused))
            {
                Summary = null;
            }
        }
    }
}

/// <summary>The limits of one Codex home the person added.</summary>
public sealed class CodexProfileState
{
    public CodexProfileState(CodexAccountProfile profile) => Profile = profile;

    public CodexAccountProfile Profile { get; set; }

    public AgentLimits? Limits { get; set; }

    public string? Plan { get; set; }

    /// <summary>Waiting for its turn or being checked.</summary>
    public bool Checking { get; set; }

    public CodexAccountError? Failure { get; set; }

    /// <summary>When the last check began; null until one has.</summary>
    public DateTime? Attempted { get; set; }

    public string Id => Profile.Id;

    public CodexProfileState Copy() => (CodexProfileState)MemberwiseClone();

    /// <summary>An expired window is unknown, not a freshly replenished allowance.</summary>
    public AgentLimits? CurrentLimits(DateTime now)
    {
        if (Limits is null)
        {
            return null;
        }

        var windows = Limits.Windows.Where(w => w.ResetsAt is not { } r || r > now).ToList();
        return windows.Count == 0 ? null : Limits with { Windows = windows };
    }

    public bool IsDue(DateTime now)
    {
        if (Attempted is not { } attempted)
        {
            return true;
        }

        // Only a window that renewed after the reading asks sooner, so a Codex that answers with a past renewal is not asked every minute.
        var renewed = Limits is { } reading && reading.Windows.Any(w => w.ResetsAt is { } r && r > reading.ObservedAt && r <= now);
        return now - attempted >= CodexAccountUsageReader.Interval(Failure, renewed);
    }
}

/// <summary>
/// The limits of each Codex home the person added, kept apart from the provider-wide reading of the logs and in memory only. One home is
/// checked at a time: when the AI page shows them and they are due, or when a card or Settings asks, and never while the island is away
/// or Codex is off.
/// </summary>
public sealed class CodexProfileLimitsService
{
    private readonly AgentSettings _settings;
    private readonly object _gate = new();
    private List<CodexProfileState> _states = [];
    private (string Id, CancellationTokenSource Cancel)? _running;
    private readonly List<(string Id, bool Asked)> _waiting = [];
    private bool _visible;
    private string? _shellPath;
    private Timer? _timer;

    public CodexProfileLimitsService(AgentSettings settings) => _settings = settings;

    /// <summary>Raised (any thread) when a state changed.</summary>
    public event Action? Changed;

    /// <summary>Copies of the states, safe to read from any thread.</summary>
    public List<CodexProfileState> States
    {
        get
        {
            lock (_gate)
            {
                return _states.Select(s => s.Copy()).ToList();
            }
        }
    }

    /// <summary>Only while an account card can show: a hidden Limits card asks nothing.</summary>
    public bool IsEnabled => _settings.IsEnabled() && _settings.ShowsCodexAccounts();

    /// <summary>Follows the saved homes and whether Codex is on. Asks only what is due, and only while the page shows.</summary>
    public void Synchronize()
    {
        if (!IsEnabled)
        {
            CancelChecks();
        }

        var wanted = CodexAccountProfile.Decode(_settings.App.Get(AgentKeys.CodexProfiles, ""));
        var refresh = false;
        lock (_gate)
        {
            if (!_states.Select(s => (s.Profile.Id, s.Profile.Name, s.Profile.Directory)).SequenceEqual(wanted.Select(p => (p.Id, p.Name, p.Directory))))
            {
                var previous = _states;
                _states = wanted.Select(profile =>
                {
                    // A renamed home keeps its reading and its check.
                    var existing = previous.FirstOrDefault(s => s.Id == profile.Id && s.Profile.Directory == profile.Directory);
                    if (existing is null)
                    {
                        return new CodexProfileState(profile);
                    }

                    existing.Profile = profile;
                    return existing;
                }).ToList();
                var kept = _states.Where(s => s.Checking).Select(s => s.Id).ToHashSet();
                _waiting.RemoveAll(w => !kept.Contains(w.Id));
                if (_running is { } r && !kept.Contains(r.Id))
                {
                    r.Cancel.Cancel();
                    _running = null;
                }
            }

            refresh = _visible;
        }

        SyncTimer();
        if (refresh)
        {
            Refresh();
        }

        StartNext();
        Changed?.Invoke();
    }

    /// <summary>A Settings preview shows what is known without asking.</summary>
    public void PageDidAppear(bool preview = false)
    {
        if (!preview)
        {
            _visible = true;
        }

        Synchronize();
    }

    /// <summary>A check under way finishes and keeps its time, so looking at the page again does not start it over.</summary>
    public void PageDidDisappear()
    {
        _visible = false;
        SyncTimer();
        lock (_gate)
        {
            var dropped = _waiting.Where(w => !w.Asked).Select(w => w.Id).ToHashSet();
            if (dropped.Count == 0)
            {
                return;
            }

            _waiting.RemoveAll(w => !w.Asked);
            foreach (var s in _states.Where(s => dropped.Contains(s.Id)))
            {
                s.Checking = false;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The island is away, the section stops or Codex is off: nothing runs.</summary>
    public void Pause()
    {
        _visible = false;
        CancelChecks();
        SyncTimer();
    }

    /// <summary>Queues the homes that are due, or, when the person asks, <paramref name="id"/> or every home.</summary>
    public void Refresh(string? id = null, bool force = false, DateTime? now = null)
    {
        if (!IsEnabled || !(_visible || force))
        {
            return;
        }

        var t = now ?? DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var state in _states.Where(s => id is null || s.Id == id))
            {
                if (state.Checking)
                {
                    if (force)
                    {
                        var place = _waiting.FindIndex(w => w.Id == state.Id);
                        if (place >= 0)
                        {
                            _waiting[place] = (state.Id, true);
                        }
                    }

                    continue;
                }

                if (!force && !state.IsDue(t))
                {
                    continue;
                }

                state.Checking = true;
                _waiting.Add((state.Id, force));
            }
        }

        StartNext();
        Changed?.Invoke();
    }

    private void StartNext()
    {
        CodexAccountProfile profile;
        CancellationTokenSource cancel;
        string? known;
        bool searching;
        while (true)
        {
            lock (_gate)
            {
                if (_running is not null || _waiting.Count == 0)
                {
                    return;
                }

                var next = _waiting[0];
                _waiting.RemoveAt(0);
                var state = _states.FirstOrDefault(s => s.Id == next.Id);
                if (state is null)
                {
                    continue;
                }

                profile = state.Profile;
                searching = next.Asked && state.Failure == CodexAccountError.MissingCli && _shellPath is null;
                state.Attempted = DateTime.UtcNow;
                cancel = new CancellationTokenSource();
                _running = (next.Id, cancel);
                known = _shellPath;
                break;
            }
        }

        _ = Task.Run(() =>
        {
            var path = searching ? CodexServer.WhereCodexPath() : known;
            (CodexAccountReading? Reading, CodexAccountError? Error) result;
            if (CodexServer.Executable(AgentLogRoot.Home, path) is { } executable)
            {
                result = CodexAccountUsageReader.Read(profile, executable, path, cancel.Token);
            }
            else
            {
                result = (null, CodexAccountError.MissingCli);
            }

            lock (_gate)
            {
                if (_running is not { } r || !ReferenceEquals(r.Cancel, cancel))
                {
                    return;
                }

                _running = null;
                if (path is not null)
                {
                    _shellPath = path;
                }

                Finish(profile, result);
            }

            Changed?.Invoke();
            StartNext();
        });
    }

    private void Finish(CodexAccountProfile profile, (CodexAccountReading? Reading, CodexAccountError? Error) result)
    {
        var state = _states.FirstOrDefault(s => s.Id == profile.Id && s.Profile.Directory == profile.Directory);
        if (state is null)
        {
            return;
        }

        state.Checking = false;
        if (result.Reading is { } reading)
        {
            state.Limits = reading.Limits;
            state.Plan = reading.Plan;
            state.Failure = null;
        }
        else if (result.Error == CodexAccountError.Cancelled)
        {
            state.Attempted = null;
        }
        else if (result.Error is { } failure)
        {
            // Another sign-in may be behind the home now. A check that could not finish leaves the last reading standing.
            if (failure is CodexAccountError.NeedsSignIn or CodexAccountError.InvalidProfile)
            {
                state.Limits = null;
                state.Plan = null;
            }

            state.Failure = failure;
        }
    }

    private void CancelChecks()
    {
        lock (_gate)
        {
            if (_running is null && _waiting.Count == 0)
            {
                return;
            }

            if (_running is { } r)
            {
                r.Cancel.Cancel();
                // Asked again once checks can run.
                var state = _states.FirstOrDefault(s => s.Id == r.Id);
                if (state is not null)
                {
                    state.Attempted = null;
                }
            }

            _running = null;
            _waiting.Clear();
            foreach (var s in _states.Where(s => s.Checking))
            {
                s.Checking = false;
            }
        }

        Changed?.Invoke();
    }

    private void SyncTimer()
    {
        bool need;
        lock (_gate)
        {
            need = _visible && IsEnabled && _states.Count > 0;
        }

        if (!need)
        {
            _timer?.Dispose();
            _timer = null;
            return;
        }

        _timer ??= new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
}
