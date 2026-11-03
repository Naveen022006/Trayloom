// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>One tile of the AI page grid.</summary>
public sealed record AgentTile(AgentCard Card, AgentProvider? Provider, string? AccountId = null, bool NamesDefaultAccount = false)
{
    /// <summary>The height this card needs for what it shows now (the sessions listed, the limits and notes); null keeps the default.</summary>
    public double? HeightHint { get; init; }

    public string Id => Card.Raw() + (Provider is { } p ? "." + p.Raw() : "") + (AccountId is { } a ? "." + a : "");
}

/// <summary>The closed island's reading: a lead (the time), the limit beside it, and the window the limit is about (for its tint).</summary>
public sealed record StripText(string Lead, string? Limit, LimitWindow? Window, AgentProvider? Provider)
{
    public string Text => Lead.Length == 0 ? Limit ?? "" : Limit is null ? Lead : Lead + " · " + Limit;
}

/// <summary>The settings of the AI Agents extension and the pure rules built on them (Vorssaint <c>NotchAgentSupport</c>).</summary>
public sealed class AgentSettings
{
    public const double DefaultFinishMinimum = 60;
    public const double DefaultLimitThreshold = 80;

    /// <summary>The closed island shows the time and the limit together unless the person chose one of them.</summary>
    public const string DefaultReadout = "timeLimit";
    public static readonly double[] FinishMinimums = [0, 30, 60, 120, 300];
    public static readonly double[] LimitThresholds = [50, 75, 80, 90, 95];
    public static readonly double[] Budgets = [0, 5, 10, 25, 50, 100, 250];

    /// <summary>A turn silent for this long is not shown as working.</summary>
    public static readonly TimeSpan IdleTurn = TimeSpan.FromMinutes(10);

    private readonly IAppSettings _app;
    private readonly IFeatureRuntime _runtime;

    public AgentSettings(IAppSettings app, IFeatureRuntime runtime)
    {
        _app = app;
        _runtime = runtime;
        RegisterDefaults(app);
    }

    public IAppSettings App => _app;

    public static void RegisterDefaults(IAppSettings app)
    {
        app.Register(AgentKeys.Enabled, false);
        app.Register(AgentKeys.Claude, true);
        app.Register(AgentKeys.Codex, true);
        app.Register(AgentKeys.OpenCode, true);
        app.Register(AgentKeys.Copilot, true);
        app.Register(AgentKeys.ClaudeProfile, "");
        app.Register(AgentKeys.CodexProfiles, "");
        app.Register(AgentKeys.CardOrder, "");
        app.Register(AgentKeys.HiddenCards, "");
        app.Register(AgentKeys.Period, "today");
        app.Register(AgentKeys.LimitDisplay, "remaining");
        app.Register(AgentKeys.LimitFocus, "mostUsed");
        app.Register(AgentKeys.LiveActivity, true);
        app.Register(AgentKeys.Readout, DefaultReadout);
        app.Register(AgentKeys.FinishAlert, true);
        app.Register(AgentKeys.FinishMinimum, DefaultFinishMinimum);
        app.Register(AgentKeys.LimitAlert, true);
        app.Register(AgentKeys.LimitThreshold, DefaultLimitThreshold);
        app.Register(AgentKeys.DailyBudget, 0.0);
        app.Register(AgentKeys.PriceUpdates, true);
        app.Register(AgentKeys.Attention, true);
    }

    /// <summary>The island is installed, the AI section is switched on and not hidden from the island.</summary>
    public bool IsEnabled() => _runtime.IsAvailable("notch") && SectionShows();

    /// <summary>The AI section is installed, switched on and shown, whether or not the island itself is.</summary>
    public bool SectionShows() =>
        _runtime.IsAvailable(AgentKeys.FeatureId) && _app.Get(AgentKeys.Enabled, false)
        && !_app.Get(AgentKeys.HiddenModules, "").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(AgentKeys.ModuleId);

    public List<AgentProvider> Providers() => AgentProviders.All.Where(p => _app.Get(p.SettingKey(), true)).ToList();

    public List<AgentCard> OrderedCards()
    {
        var stored = _app.Get(AgentKeys.CardOrder, "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(AgentCards.Parse).Where(c => c is not null).Select(c => c!.Value);
        var seen = new HashSet<AgentCard>();
        return stored.Concat(AgentCards.All).Where(seen.Add).ToList();
    }

    public HashSet<AgentCard> HiddenCards() =>
        _app.Get(AgentKeys.HiddenCards, "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(AgentCards.Parse).Where(c => c is not null).Select(c => c!.Value).ToHashSet();

    public List<AgentCard> Cards()
    {
        var hidden = HiddenCards();
        return OrderedCards().Where(c => !hidden.Contains(c)).ToList();
    }

    public void SetCardHidden(AgentCard card, bool hidden)
    {
        var set = _app.Get(AgentKeys.HiddenCards, "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (hidden)
        {
            set.Add(card.Raw());
        }
        else
        {
            set.Remove(card.Raw());
        }

        _app.Set(AgentKeys.HiddenCards, string.Join(',', set.OrderBy(s => s, StringComparer.Ordinal)));
    }

    public void SetCardOrder(IEnumerable<AgentCard> order) => _app.Set(AgentKeys.CardOrder, string.Join(',', order.Select(c => c.Raw())));

    /// <summary>Added Codex accounts have cards only in the Limits card's place.</summary>
    public bool ShowsCodexAccounts() => Providers().Contains(AgentProvider.Codex) && !HiddenCards().Contains(AgentCard.Limits);

    public AgentPeriod Period() => AgentPeriods.Parse(_app.Get(AgentKeys.Period, "today"));

    public bool UsedDisplay() => _app.Get(AgentKeys.LimitDisplay, "remaining") == "used";

    public AgentLimitFocus Focus() => _app.Get(AgentKeys.LimitFocus, "mostUsed") switch { "session" => AgentLimitFocus.Session, "weekly" => AgentLimitFocus.Weekly, _ => AgentLimitFocus.MostUsed };

    public bool ShowsLiveActivity() => IsEnabled() && _app.Get(AgentKeys.LiveActivity, true);

    public AgentReadout Readout() => ParseReadout(_app.Get(AgentKeys.Readout, DefaultReadout));

    /// <summary>The menu value of a stored readout (a stored word nothing knows is the default).</summary>
    public static string ReadoutKey(string raw) => raw is "elapsed" or "tokens" or "cost" or "limit" ? raw : DefaultReadout;

    public static AgentReadout ParseReadout(string raw) => raw switch
    {
        "tokens" => AgentReadout.Tokens,
        "cost" => AgentReadout.Cost,
        "limit" => AgentReadout.Limit,
        "elapsed" => AgentReadout.Elapsed,
        _ => AgentReadout.TimeAndLimit,
    };

    /// <summary>The shortest turn worth a notice when it ends; null while those are off.</summary>
    public TimeSpan? FinishMinimum()
    {
        if (!_app.Get(AgentKeys.FinishAlert, true))
        {
            return null;
        }

        var v = _app.Get(AgentKeys.FinishMinimum, DefaultFinishMinimum);
        return TimeSpan.FromSeconds(double.IsFinite(v) ? Math.Min(3600, Math.Max(0, v)) : DefaultFinishMinimum);
    }

    /// <summary>Percent used that earns a warning; null while warnings are off.</summary>
    public double? LimitThreshold()
    {
        if (!_app.Get(AgentKeys.LimitAlert, true))
        {
            return null;
        }

        var v = _app.Get(AgentKeys.LimitThreshold, DefaultLimitThreshold);
        return double.IsFinite(v) ? Math.Min(100, Math.Max(1, v)) : DefaultLimitThreshold;
    }

    public double? DailyBudget()
    {
        var v = _app.Get(AgentKeys.DailyBudget, 0.0);
        return double.IsFinite(v) && v > 0 ? v : null;
    }

    /// <summary>Whether a session waiting for the person raises an island notice (the Now card and strip always show the state).</summary>
    public bool AttentionAlerts() => _app.Get(AgentKeys.Attention, true);

    public bool UpdatesPrices() => _app.Get(AgentKeys.PriceUpdates, true);

    public string IdleContent() => _app.Get(AgentKeys.IdleContent, "music");

    /// <summary>Whether anything shows or alerts on the plan limits.</summary>
    public bool LimitsInUse(bool pageShown) =>
        LimitThreshold() is not null
        || (ShowsLiveActivity() && Readout() is AgentReadout.Limit or AgentReadout.TimeAndLimit)
        || IdleContent() == "agents"
        || (pageShown && !HiddenCards().Contains(AgentCard.Limits));

    // ------------------------------------------------------------------------------------------ rules

    /// <summary>The allowance the closed island shows: the window the person chose, or the one closest to running out.</summary>
    public static LimitWindow? FocusedLimit(AgentLimits? limits, AgentLimitFocus focus, DateTime now) =>
        ChosenLimit(limits, focus, now) ?? AgentLimitSupport.Binding(limits, now);

    private static LimitWindow? ChosenLimit(AgentLimits? limits, AgentLimitFocus focus, DateTime now)
    {
        LimitKind kind;
        switch (focus)
        {
            case AgentLimitFocus.Session:
                kind = LimitKind.Session;
                break;
            case AgentLimitFocus.Weekly:
                kind = LimitKind.Weekly;
                break;
            default:
                return null;
        }

        var w = limits?.Windows.FirstOrDefault(x => x.Kind == kind && x.Scope is null);
        return w is null ? null : AgentLimitSupport.Current(w, now);
    }

    /// <summary>The resting island's allowance across every account.</summary>
    public static (AgentProvider Provider, LimitWindow Window)? RestingLimit(AgentUsageSnapshot snapshot, AgentLimitFocus focus, DateTime now)
    {
        (AgentProvider Provider, LimitWindow Window)? MostSpent(Func<AgentLimits, LimitWindow?> pick)
        {
            (AgentProvider Provider, LimitWindow Window)? best = null;
            foreach (var (provider, limits) in snapshot.Limits)
            {
                if (pick(limits) is not { Renewed: false } w)
                {
                    continue;
                }

                if (best is null || w.UsedPercent > best.Value.Window.UsedPercent
                    || (w.UsedPercent == best.Value.Window.UsedPercent && string.CompareOrdinal(provider.Raw(), best.Value.Provider.Raw()) < 0))
                {
                    best = (provider, w);
                }
            }

            return best;
        }

        return MostSpent(l => ChosenLimit(l, focus, now)) ?? MostSpent(l => AgentLimitSupport.Binding(l, now));
    }

    public static double StripTextSize(double height) => Math.Min(15, height - 7);

    /// <summary>"Wk 100%" (used) or "Wk 0% left" (left): the tag says which window, the words which share, so a bare number is never ambiguous.</summary>
    public static string LimitReading(LimitWindow window, bool usedDisplay)
    {
        var percent = AgentFormat.Percent(usedDisplay ? window.UsedFraction : window.RemainingFraction);
        return AgentLimitPlan.Tag(window) + " " + (usedDisplay ? percent : AgentStrings.Left(percent));
    }

    /// <summary>The focused limit of one provider as the closed island writes it; null when it has none or it renewed and nothing says what it holds now.</summary>
    public static (string Text, LimitWindow Window)? LimitPart(AgentUsageSnapshot snapshot, AgentProvider provider, bool usedDisplay, AgentLimitFocus focus, DateTime now)
    {
        if (!snapshot.Limits.TryGetValue(provider, out var limits) || FocusedLimit(limits, focus, now) is not { Renewed: false } window)
        {
            return null;
        }

        return (LimitReading(window, usedDisplay), window);
    }

    /// <summary>The longest "time · limit" reading the closed island has room for; past it the limit stands alone.</summary>
    private const int StripRoom = 24;

    /// <summary>The time and the limit together, or the limit alone when the readout asks for that or the pair would not fit the capsule.</summary>
    public static StripText Join(AgentReadout readout, string time, (string Text, LimitWindow Window) limit, AgentProvider provider)
    {
        var alone = readout == AgentReadout.Limit || time.Length + 3 + limit.Text.Length > StripRoom;
        return new StripText(alone ? "" : time, limit.Text, limit.Window, provider);
    }

    /// <summary>What the strip shows beside the island centre while agents work.</summary>
    public static string StripReading(AgentUsageSnapshot snapshot, AgentReadout readout, bool usedDisplay, AgentLimitFocus focus, DateTime now) =>
        Strip(snapshot, readout, usedDisplay, focus, now).Text;

    /// <summary>The strip's reading in its parts, so the limit can be tinted by how much is left of it.</summary>
    public static StripText Strip(AgentUsageSnapshot snapshot, AgentReadout readout, bool usedDisplay, AgentLimitFocus focus, DateTime now)
    {
        var live = snapshot.Live;

        string Elapsed() => AgentFormat.Clock((now - (live.Count > 0 ? live.Min(l => l.Started) : now)).TotalSeconds);

        switch (readout)
        {
            case AgentReadout.Elapsed:
                return new StripText(Elapsed(), null, null, null);
            case AgentReadout.Tokens:
                return new StripText(AgentFormat.Tokens(live.Sum(l => l.Tokens.Output)), null, null, null);
            case AgentReadout.Cost:
                return new StripText(AgentFormat.Cost(live.Sum(l => l.Cost)), null, null, null);
            default:
            {
                foreach (var provider in AgentProviders.All.Where(p => live.Any(l => l.Provider == p)))
                {
                    if (LimitPart(snapshot, provider, usedDisplay, focus, now) is { } part)
                    {
                        return Join(readout, Elapsed(), part, provider);
                    }
                }

                return new StripText(Elapsed(), null, null, null);
            }
        }
    }

    /// <summary>Settings must illustrate the selected reading even before any agent has reported an allowance (example data stays in this copy).</summary>
    public static AgentUsageSnapshot StripSampleSnapshot(AgentUsageSnapshot snapshot, AgentProvider provider, AgentLimitFocus focus, DateTime now)
    {
        var sample = new AgentUsageSnapshot
        {
            Loaded = snapshot.Loaded,
            Now = snapshot.Now,
            Limits = new Dictionary<AgentProvider, AgentLimits>(snapshot.Limits),
            Plans = new Dictionary<AgentProvider, AgentPlan>(snapshot.Plans),
            Live = [.. snapshot.Live],
        };
        if (sample.Live.Count == 0)
        {
            sample.Live.Add(new LiveSession { Id = "example", Provider = provider, Started = now.AddSeconds(-754), LastActivity = now, Tokens = new AgentTokens(1_180_000, 0, 0, 20_000, 0), Cost = 4.56 });
        }

        var working = AgentProviders.All.FirstOrDefault(p => sample.Live.Any(l => l.Provider == p));
        sample.Limits.TryGetValue(working, out var existing);
        if (FocusedLimit(existing, focus, now) is null)
        {
            sample.Plans.TryGetValue(working, out var plan);
            var windows = new List<LimitWindow>();
            if (AgentLimitPlan.HasSessionWindow(working, plan))
            {
                windows.Add(new LimitWindow("example-session", LimitKind.Session, 300, null, 32, now.AddHours(1)));
            }

            windows.Add(new LimitWindow("example-week", LimitKind.Weekly, 10_080, null, 57, now.AddDays(1)));
            sample.Limits[working] = new AgentLimits(working, windows, now, LimitSource.SessionLog);
        }

        return sample;
    }

    /// <summary>A limit's share on the island's card. It names which share it is.</summary>
    public static string LimitShare(LimitWindow window, bool usedDisplay) =>
        usedDisplay ? AgentStrings.UsedShare(AgentFormat.Percent(window.UsedFraction)) : AgentStrings.Left(AgentFormat.Percent(window.RemainingFraction));

    /// <summary>Every digit takes the same width, so a reading's shape, not its value, sets the strip's width.</summary>
    public static string ReadingShape(string reading) => new(reading.Select(c => char.IsDigit(c) ? '0' : c).ToArray());

    // ------------------------------------------------------------------------------------------ layout

    public const double Spacing = 10;
    public const double CardHeight = 96;
    public const double ChartHeight = 118;
    public const double AccountCardHeight = 116;
    public static readonly TimeSpan AccountStaleness = TimeSpan.FromMinutes(10);
    public const double PairWidth = 390;

    /// <summary>Codex homes the person added take the place of Codex's one limits card.</summary>
    public static List<AgentTile> Tiles(IEnumerable<AgentCard> cards, IReadOnlyList<AgentProvider> providers, IReadOnlyList<string>? codexAccounts = null, string? claudeAccount = null)
    {
        codexAccounts ??= [];
        var tiles = new List<AgentTile>();
        foreach (var card in cards)
        {
            switch (card)
            {
                case AgentCard.Limits:
                    foreach (var provider in providers.Where(p => p.ReportsLimits()))
                    {
                        if (provider == AgentProvider.Claude && claudeAccount is not null)
                        {
                            tiles.Add(new AgentTile(AgentCard.Limits, provider, claudeAccount));
                        }
                        else if (provider == AgentProvider.Codex && codexAccounts.Count > 0)
                        {
                            tiles.AddRange(codexAccounts.Select(a => new AgentTile(AgentCard.Limits, provider, a)));
                        }
                        else
                        {
                            tiles.Add(new AgentTile(AgentCard.Limits, provider));
                        }
                    }

                    break;
                case AgentCard.Resets:
                    if (providers.Contains(AgentProvider.Codex))
                    {
                        tiles.Add(new AgentTile(AgentCard.Resets, AgentProvider.Codex, null, codexAccounts.Count > 0));
                    }

                    break;
                default:
                    tiles.Add(new AgentTile(card, null));
                    break;
            }
        }

        return tiles;
    }

    /// <summary>Cards pair up in reading order; a chart, or a card left without a partner, takes the whole row.</summary>
    public static List<List<AgentTile>> Rows(IEnumerable<AgentTile> tiles, double width)
    {
        var pairs = width >= PairWidth;
        var rows = new List<List<AgentTile>>();
        AgentTile? waiting = null;
        foreach (var tile in tiles)
        {
            if (tile.Card.FullWidth() || !pairs)
            {
                if (waiting is not null)
                {
                    rows.Add([waiting]);
                }

                waiting = null;
                rows.Add([tile]);
            }
            else if (waiting is { } partner)
            {
                rows.Add([partner, tile]);
                waiting = null;
            }
            else
            {
                waiting = tile;
            }
        }

        if (waiting is not null)
        {
            rows.Add([waiting]);
        }

        return rows;
    }

    public static double Height(IReadOnlyList<AgentTile> row)
    {
        var hint = row.Where(t => t.HeightHint is not null).Select(t => t.HeightHint!.Value).DefaultIfEmpty(0).Max();
        if (row.Any(t => t.Card.FullWidth() && t.HeightHint is null))
        {
            return Math.Max(hint, ChartHeight);
        }

        return Math.Max(hint, row.Any(t => t.AccountId is not null || t.NamesDefaultAccount) ? AccountCardHeight : CardHeight);
    }

    public static double ContentHeight(IReadOnlyList<List<AgentTile>> rows) =>
        rows.Count == 0 ? 0 : rows.Sum(r => Height(r)) + Spacing * (rows.Count - 1);

    /// <summary>An account card's line in place of its windows while it has none, or under them.</summary>
    public static (string? Placeholder, string? Caption) AccountNotes(bool hasWindows, bool checking, string? failure, string hint, string loading, string? updated) =>
        !hasWindows ? (checking ? loading : failure ?? hint, null) : (null, failure ?? updated);

    /// <summary>A reading old enough to have missed use elsewhere shows quieter.</summary>
    public static bool LimitsAreStale(AgentLimits? limits, AgentProvider provider, bool account, DateTime now)
    {
        if (limits is null)
        {
            return false;
        }

        var age = now - limits.ObservedAt;
        return account ? age >= AccountStaleness : provider == AgentProvider.Claude && age >= ClaudeAppUsage.Freshness;
    }
}
