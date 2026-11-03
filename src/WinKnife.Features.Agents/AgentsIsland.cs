// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Agents;

/// <summary>The closed island while an agent works (capsule form): the marks of the working agents and one reading (spec 03 A7.2).</summary>
public sealed class AgentStripView : StackPanel
{
    private readonly AgentsRuntime _runtime;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _reading;
    private readonly AgentReadout _readout;
    private string _shape = "";
    private string _tip = "";
    private string _names = "";
    private readonly bool _waiting;

    /// <summary>
    /// Creates the strip from the latest snapshot. <paramref name="snapshotOverride"/> and <paramref name="providerOverride"/> let the settings sample
    /// draw example data.
    /// </summary>
    public AgentStripView(AgentsRuntime runtime, double textSize = 13, AgentUsageSnapshot? snapshotOverride = null)
    {
        _runtime = runtime;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        var snapshot = snapshotOverride ?? runtime.Service.Snapshot;
        var live = snapshot.Live;
        var sessions = snapshotOverride is null ? runtime.Sessions.Current.Where(s => s.State != AgentSessionState.Idle).ToList() : [];
        _waiting = sessions.Any(s => s.IsWaiting);
        var working = AgentProviders.All.Where(p => live.Any(l => l.Provider == p) || sessions.Any(s => s.Provider == p)).ToList();
        _readout = runtime.Settings.Readout();
        var tint = _waiting ? AgentColors.Orange : working.Count > 0 ? working[0].Tint() : Colors.White;
        var size = working.Count > 1 ? 10.0 : 12.0;
        var marks = new StackPanel { Orientation = Orientation.Horizontal };
        if (_waiting)
        {
            // Amber pulse: a session waits for the person.
            marks.Children.Add(new AgentPulse(AgentColors.Orange, 4) { Margin = new Thickness(0, 0, 2, 0) });
        }

        foreach (var p in working)
        {
            marks.Children.Add(new AgentGlyph(p, size));
        }

        Children.Add(marks);
        _reading = AU.Text("", textSize, FontWeights.SemiBold, AgentColors.Of(tint), TextTrimming.None, false, true);
        _reading.Margin = new Thickness(6, 0, 0, 0);
        Children.Add(_reading);
        Update(snapshotOverride);
        _names = string.Join(", ", working.Select(w => w.DisplayName()));
        AU.Tip(this, _names);
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            if (_readout is AgentReadout.Elapsed or AgentReadout.Limit or AgentReadout.TimeAndLimit)
            {
                _timer.Start();
            }

            runtime.Service.Changed += OnChanged;
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            runtime.Service.Changed -= OnChanged;
        };
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    private AgentUsageSnapshot? _fixed;

    private void OnTick(object? sender, EventArgs e) => Update(_fixed);

    private void OnChanged() => Dispatcher.BeginInvoke(() => Update(_fixed));

    private void Update(AgentUsageSnapshot? snapshotOverride)
    {
        _fixed = snapshotOverride;
        var settings = _runtime.Settings;
        var snapshot = snapshotOverride ?? _runtime.Service.Snapshot;
        var used = settings.UsedDisplay();
        var focus = settings.Focus();
        var now = DateTime.UtcNow;
        var info = AgentSettings.Strip(snapshot, _readout, used, focus, now);
        if (snapshotOverride is null)
        {
            var sessions = _runtime.Sessions.Current.Where(s => s.State != AgentSessionState.Idle).ToList();
            if (sessions.Any(s => s.IsWaiting))
            {
                info = new StripText(Loc.T("Needs you"), null, null, null);
            }
            else if (snapshot.Live.Count == 0 && sessions.Count > 0)
            {
                // Sessions found from their files have no token counts yet: the clock is the one reading they can give, and the limit of the agent that works.
                var clock = AgentFormat.Clock((now - sessions.Min(s => s.Since)).TotalSeconds);
                info = new StripText(clock, null, null, null);
                if (_readout is AgentReadout.Limit or AgentReadout.TimeAndLimit)
                {
                    foreach (var provider in AgentProviders.All.Where(p => sessions.Any(s => s.Provider == p)))
                    {
                        if (AgentSettings.LimitPart(snapshot, provider, used, focus, now) is { } part)
                        {
                            info = AgentSettings.Join(_readout, clock, part, provider);
                            break;
                        }
                    }
                }
            }
        }

        _reading.Inlines.Clear();
        if (info.Lead.Length > 0)
        {
            _reading.Inlines.Add(new Run(info.Lead));
        }

        if (info.Limit is not null)
        {
            if (info.Lead.Length > 0)
            {
                _reading.Inlines.Add(new Run(" · ") { Foreground = AgentColors.White(0.45) });
            }

            // The limit says how close it is: the agent's colour while there is room, orange then red as it runs out.
            var window = info.Window;
            var tint = window is null || info.Provider is not { } provider ? AgentColors.Of(AgentColors.Orange) : AgentColors.Of(AgentColors.LimitTint(provider, window.UsedFraction));
            _reading.Inlines.Add(new Run(info.Limit) { Foreground = _waiting ? AgentColors.Of(AgentColors.Orange) : tint });
        }

        if (_waiting)
        {
            _reading.Foreground = AgentColors.Of(AgentColors.Orange);
        }

        var tip = info.Window is { } w
            ? _names + "\n" + AgentLimitPlan.Name(w) + ": " + AgentSettings.LimitShare(w, used) + (w.UsedPercent >= 100 ? " · " + AgentStrings.LimitReached : "")
              + (AgentLimitSupport.DisplayedReset(w, now) is not null ? "\n" + Loc.T("resets") + " " + LimitPresenter.ResetText(w, now, false) : "")
            : _names;
        if (tip != _tip)
        {
            _tip = tip;
            AU.Tip(this, tip);
        }

        // A reading that gains a digit, like an hour passing, needs a wider island; the island measures the same reading.
        var shape = AgentSettings.ReadingShape(info.Text);
        if (shape != _shape)
        {
            var first = _shape.Length == 0;
            _shape = shape;
            if (!first)
            {
                _runtime.IslandContext?.Invalidate();
            }
        }
    }
}

/// <summary>A notice for the closed island: mark + title and detail (text 11 medium, mono digits).</summary>
public sealed class AgentNoticeView : StackPanel
{
    public AgentNoticeView(AgentNotice notice)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        var tint = AgentColors.Of(notice.Tint);
        if (notice.Provider is { } provider && notice.Glyph.Length == 0)
        {
            Children.Add(new AgentGlyph(provider, 11, true));
        }
        else
        {
            var icon = AU.Icon(notice.Glyph, 12, tint);
            icon.Margin = new Thickness(2, 0, 4, 0);
            Children.Add(icon);
        }

        var title = AU.Text(notice.Title, 11, FontWeights.Medium, AgentColors.Primary, TextTrimming.CharacterEllipsis, false, true);
        title.Margin = new Thickness(4, 0, 8, 0);
        title.MaxWidth = 170;
        Children.Add(title);
        var detail = AU.Text(notice.Detail, 11, FontWeights.Medium, tint, TextTrimming.CharacterEllipsis, false, true);
        detail.MaxWidth = 120;
        Children.Add(detail);
    }
}

/// <summary>The resting island's wings: the chosen allowance as a ring and a number, or today's API value when none is known (spec 03 A7.3).</summary>
public sealed class AgentRestingView : StackPanel
{
    private readonly AgentsRuntime _runtime;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };

    public AgentRestingView(AgentsRuntime runtime)
    {
        _runtime = runtime;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Build();
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            _timer.Start();
            runtime.Service.Changed += OnChanged;
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            runtime.Service.Changed -= OnChanged;
        };
    }

    private void OnTick(object? sender, EventArgs e) => Build();

    private void OnChanged() => Dispatcher.BeginInvoke(Build);

    private void Build()
    {
        Children.Clear();
        var settings = _runtime.Settings;
        var snapshot = _runtime.Service.Snapshot;
        var now = DateTime.UtcNow;
        var used = settings.UsedDisplay();
        if (AgentSettings.RestingLimit(snapshot, settings.Focus(), now) is { } limit)
        {
            // The same rules as the closed strip: a tag says which window, the words which share, and the tint how close it is to running out.
            var window = limit.Window;
            var tint = AgentColors.LimitTint(limit.Provider, window.UsedFraction);
            var exhausted = window.UsedPercent >= 100;
            var value = exhausted ? 1 : used ? window.UsedFraction : window.RemainingFraction;
            var ring = new AgentRing(value, tint, 2) { Width = 11, Height = 11 };
            AU.Tip(ring, limit.Provider.DisplayName() + " · " + AgentLimitPlan.Name(window) + ": " + AgentSettings.LimitShare(window, used) + (exhausted ? " · " + AgentStrings.LimitReached : ""));
            Children.Add(ring);
            var text = AU.Text(AgentSettings.LimitReading(window, used), 9, FontWeights.Medium, window.UsedFraction >= 0.8 ? AgentColors.Of(tint) : AgentColors.Primary, TextTrimming.None, false, true);
            text.Margin = new Thickness(5, 0, 0, 0);
            Children.Add(text);
        }
        else if (AgentProviders.All.FirstOrDefault(snapshot.Seen.Contains) is var provider && snapshot.Seen.Contains(provider))
        {
            Children.Add(AgentMarks.Mark(provider, 10));
            var text = AU.Text(AgentFormat.Cost(snapshot.Usage(AgentPeriod.Today).Total.Cost), 9, FontWeights.Medium, AgentColors.Primary, TextTrimming.None, false, true);
            text.Margin = new Thickness(5, 0, 0, 0);
            Children.Add(text);
        }
    }
}

/// <summary>Placeholder shown when the extension's runtime is not running (uninstalled or still starting).</summary>
internal static class AgentsFallback
{
    public static FrameworkElement Page() => AgentsPageView.EmptyView("sparkles", AgentStrings.Summary);
}

// ---------------------------------------------------------------------------------------------- contributions

/// <summary>The "AI Agents" page of the expanded island.</summary>
public sealed class AgentsIslandSection : IIslandSection
{
    public string? FeatureId => AgentKeys.FeatureId;

    public int Order => 150;

    public string Id => AgentKeys.ModuleId;

    public string Title => AgentStrings.Title;

    /// <summary>Sparkles have no stock glyph; this is the closest Segoe Fluent Icons symbol.</summary>
    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<AgentsRuntime>() is not { } runtime)
        {
            return AgentsFallback.Page();
        }

        runtime.IslandContext = context;
        return new AgentsPageView(runtime);
    }
}

/// <summary>The live activity of the closed island: shown while at least one agent turn is open and the strip is on.</summary>
public sealed class AgentsLiveStrip : IIslandStrip
{
    public string? FeatureId => AgentKeys.FeatureId;

    /// <summary>Priority list: timer, watch, downloads, <b>agents</b>, calendar, music, keep awake.</summary>
    public int Order => 40;

    public string Id => "agents";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AgentsRuntime>() is not { } runtime)
        {
            return null;
        }

        runtime.IslandContext = context;
        if (!runtime.Settings.ShowsLiveActivity()
            || (runtime.Service.Snapshot.Live.Count == 0 && !runtime.Sessions.Current.Any(s => s.State != AgentSessionState.Idle)))
        {
            return null;
        }

        return new AgentStripView(runtime);
    }
}

/// <summary>"Claude needs your permission": shown the moment a session starts waiting for the person; a click brings its window forward.</summary>
public sealed class AgentsAttentionStrip : IIslandStrip
{
    public string? FeatureId => AgentKeys.FeatureId;

    public int Order => 39;

    public string Id => "agents.attention";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AgentsRuntime>() is not { } runtime || !runtime.Settings.IsEnabled() || runtime.TakeAttention() is not { } attention)
        {
            return null;
        }

        runtime.IslandContext = context;
        return new AgentAttentionView(attention, () => runtime.FocusSession(attention));
    }
}

/// <summary>Finish, limit warning, renewal and budget notices (event <c>agents</c>, priority 1, 5 seconds).</summary>
public sealed class AgentsNoticeStrip : IIslandStrip
{
    public string? FeatureId => AgentKeys.FeatureId;

    public int Order => 41;

    public string Id => "agents.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AgentsRuntime>() is not { } runtime)
        {
            return null;
        }

        runtime.IslandContext = context;
        return runtime.Notice is { } notice && runtime.Settings.IsEnabled() ? new AgentNoticeView(notice) : null;
    }
}

/// <summary>The resting island with "AI limits" as its idle content.</summary>
public sealed class AgentsRestingStrip : IIslandStrip
{
    public string? FeatureId => AgentKeys.FeatureId;

    public int Order => 900;

    public string Id => "agents.resting";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AgentsRuntime>() is not { } runtime)
        {
            return null;
        }

        runtime.IslandContext = context;
        var settings = runtime.Settings;
        if (!settings.IsEnabled() || settings.IdleContent() != "agents" || runtime.Service.Snapshot.Live.Count > 0 || runtime.Notice is not null)
        {
            return null;
        }

        return new AgentRestingView(runtime);
    }
}
