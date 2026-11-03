// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>A transient notice for the closed island (event <c>agents</c>, priority 1, 5 seconds; spec 03 Part B 7.7).</summary>
public sealed record AgentNotice(string Title, string Detail, AgentProvider? Provider, string Glyph, System.Windows.Media.Color Tint, bool Celebrates, DateTime Expires)
{
    public const int Priority = 1;

    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Everything the AI Agents contributions share: the settings view, the usage service, the Codex services and the notice state.
/// Published through the service hub so the stateless island and settings contributions can reach the running feature.
/// </summary>
public sealed class AgentsRuntime : IDisposable
{
    private readonly IFeatureContext _context;
    private readonly DispatcherTimer _noticeTimer;
    private bool _started;

    public AgentsRuntime(IFeatureContext context)
    {
        _context = context;
        Settings = new AgentSettings(context.App, context.Runtime);
        Service = new AgentUsageService(Settings, context.Ui, context.Log);
        Resets = new CodexResetService(Service);
        Accounts = new CodexProfileLimitsService(Settings);
        Sessions = new AgentSessionMonitor(AgentSessionPaths.Default());
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _noticeTimer.Tick += (_, _) =>
        {
            if (Notice is { } n && DateTime.UtcNow >= n.Expires)
            {
                Notice = null;
                NoticeChanged?.Invoke();
            }
        };
    }

    public AgentSettings Settings { get; }

    public AgentUsageService Service { get; }

    public CodexResetService Resets { get; }

    public CodexProfileLimitsService Accounts { get; }

    /// <summary>The live sessions of Claude Code and Codex (the Now card, the closed strip and the permission notices).</summary>
    public AgentSessionMonitor Sessions { get; }

    private readonly Queue<AgentAttention> _attention = new();

    /// <summary>The session that most recently started waiting for the person (kept so the page can show it).</summary>
    public AgentAttention? LastAttention { get; private set; }

    /// <summary>Raised on the UI thread when the live sessions changed.</summary>
    public event Action? SessionsChanged;

    /// <summary>The next permission notice waiting to be shown on the closed island, once.</summary>
    public AgentAttention? TakeAttention() => _attention.Count > 0 ? _attention.Dequeue() : null;

    public bool HasAttention => _attention.Count > 0;

    /// <summary>Brings the window of the session that waits forward (the Claude app, or the terminal or editor above it).</summary>
    public bool FocusSession(AgentAttention attention)
    {
        var session = Sessions.Current.FirstOrDefault(s => s.Key == attention.Key);
        return AgentProcesses.FocusHost(session ?? new AgentSession { Origin = attention.Origin, Pid = attention.Pid, Provider = attention.Provider });
    }

    public IContributionServices Services => _context;

    /// <summary>Whether the Resets card asked "Reset the session and weekly limits now?" and waits for the click.</summary>
    public bool ResetConfirming { get; set; }

    /// <summary>The notice the closed island shows now, if any.</summary>
    public AgentNotice? Notice { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Notice"/> changed.</summary>
    public event Action? NoticeChanged;

    /// <summary>Raised on the UI thread when the island should re-render the strips (live set or notice changed).</summary>
    public event Action? StripInvalidated;

    /// <summary>The newest island context handed to a contribution; used to ask the island to re-render.</summary>
    public IIslandContext? IslandContext { get; set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (AgentHooks.RepairLegacy(AgentHooks.SettingsPath(AgentLogRoot.Home)) is { } repair)
        {
            _context.Log.Info("Claude hook: " + repair.Message);
        }

        Service.Alert += OnAlert;
        Service.LiveChanged += OnLiveChanged;
        Service.Changed += OnServiceChanged;
        Sessions.Changed += OnSessionsChanged;
        Sessions.Attention += OnAttention;
        _context.App.Changed += OnSettingChanged;
        _context.Runtime.Changed += Sync;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        Sync();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _context.App.Changed -= OnSettingChanged;
        _context.Runtime.Changed -= Sync;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        Service.Alert -= OnAlert;
        Service.LiveChanged -= OnLiveChanged;
        Service.Changed -= OnServiceChanged;
        Sessions.Changed -= OnSessionsChanged;
        Sessions.Attention -= OnAttention;
        Sessions.Stop();
        _attention.Clear();
        _noticeTimer.Stop();
        Notice = null;
        Accounts.Pause();
        Service.Stop();
    }

    public void Dispose()
    {
        Stop();
        Service.Dispose();
    }

    /// <summary>Starts, restarts or stops the services to match the settings.</summary>
    public void Sync()
    {
        Service.SyncWithPreferences();
        Accounts.Synchronize();
        var providers = Settings.Providers();
        Sessions.ClaudeEnabled = providers.Contains(AgentProvider.Claude);
        Sessions.CodexEnabled = providers.Contains(AgentProvider.Codex);
        if (Settings.IsEnabled())
        {
            Sessions.Start();
        }
        else
        {
            Sessions.Stop();
        }
    }

    private void OnSessionsChanged()
    {
        _context.Ui.BeginInvoke(() =>
        {
            SessionsChanged?.Invoke();
            StripInvalidated?.Invoke();
            IslandContext?.Invalidate();
        });
    }

    private void OnAttention(AgentAttention attention)
    {
        _context.Ui.BeginInvoke(() =>
        {
            LastAttention = attention;
            if (!Settings.AttentionAlerts() || !Settings.IsEnabled())
            {
                return;
            }

            _attention.Enqueue(attention);
            IslandContext?.Invalidate();
        });
    }

    private void OnSettingChanged(string key)
    {
        if (key.StartsWith("notchAgents", StringComparison.Ordinal) || key is AgentKeys.IdleContent or AgentKeys.HiddenModules
            || key.StartsWith("featureAvailable.", StringComparison.Ordinal))
        {
            _context.Ui.BeginInvoke(() =>
            {
                Sync();
                StripInvalidated?.Invoke();
            });
        }
    }

    private void OnServiceChanged()
    {
        IslandContext?.Invalidate();
    }

    private void OnLiveChanged()
    {
        StripInvalidated?.Invoke();
        IslandContext?.Invalidate();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Service.Pause();
                Accounts.Pause();
                break;
            case SessionSwitchReason.SessionUnlock:
                _context.Ui.BeginInvoke(() =>
                {
                    Service.Resume();
                    Sync();
                });
                break;
        }
    }

    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Service.Pause();
                Accounts.Pause();
                break;
            case PowerModes.Resume:
                _context.Ui.BeginInvoke(() =>
                {
                    Service.Resume();
                    Sync();
                });
                break;
        }
    }

    // ------------------------------------------------------------------------------------------ notices

    private void OnAlert(AgentUsageEvent e)
    {
        var show = ShowNotice(e);
        if (show is null)
        {
            return;
        }

        PostNotice(show);
    }

    /// <summary>Shows a notice (replaces the current one when its priority is at least as high).</summary>
    public void PostNotice(AgentNotice notice)
    {
        Notice = notice;
        _noticeTimer.Start();
        NoticeChanged?.Invoke();
        StripInvalidated?.Invoke();
        IslandContext?.Invalidate();
    }

    /// <summary>The notice an event produces (spec 03 A5.1): title, detail, symbol and tint.</summary>
    public AgentNotice? ShowNotice(AgentUsageEvent e)
    {
        var expires = DateTime.UtcNow + AgentNotice.Duration;
        var warn = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A);
        switch (e)
        {
            case AgentUsageEvent.Finished f:
            {
                var detail = AgentFormat.Duration(f.Duration.TotalSeconds) + (f.Cost > 0 ? " · " + AgentFormat.Cost(f.Cost) : "");
                return new AgentNotice(AgentStrings.Finished(f.Provider.DisplayName()), detail, f.Provider, "", f.Provider.Tint(), true, expires);
            }

            case AgentUsageEvent.LimitWarning w:
            {
                // At the limit the notice says so, instead of a share that reads as nothing left in one display mode and everything in the other.
                var reached = w.Window.UsedPercent >= 100;
                var share = reached ? AgentStrings.LimitReached : AgentSettings.LimitShare(w.Window, Settings.UsedDisplay());
                return new AgentNotice(LimitTitle(w.Provider, w.Window), share, w.Provider, "", reached ? System.Windows.Media.Color.FromRgb(0xFF, 0x45, 0x3A) : warn, false, expires);
            }

            case AgentUsageEvent.LimitReset r:
                return new AgentNotice(LimitTitle(r.Provider, r.Window), AgentStrings.LimitRenewed, r.Provider, "", r.Provider.Tint(), true, expires);
            case AgentUsageEvent.BudgetReached b:
                return new AgentNotice(AgentStrings.BudgetTitle, AgentFormat.Cost(b.Spent), null, "", System.Windows.Media.Color.FromRgb(0x30, 0xD1, 0x58), false, expires);
            default:
                return null;
        }
    }

    private static string LimitTitle(AgentProvider provider, LimitWindow window) => provider.DisplayName() + " · " + AgentLimitPlan.Name(window);
}

/// <summary>The installable "AI Agents" Dynamic Island extension: owns the usage engine while installed.</summary>
public sealed class AgentsFeature : IFeature
{
    private AgentsRuntime? _runtime;
    private IFeatureContext? _context;

    public string Id => AgentKeys.FeatureId;

    public void Start(IFeatureContext context)
    {
        _context = context;
        AgentSettings.RegisterDefaults(context.App);
        _runtime = new AgentsRuntime(context);
        context.Services.Provide(_runtime);
        _runtime.Start();
    }

    public void Stop()
    {
        if (_runtime is null)
        {
            return;
        }

        _context?.Services.Withdraw(_runtime);
        _runtime.Dispose();
        _runtime = null;
    }

    /// <summary>Command Bar: turn the extension on or off.</summary>
    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_context is not { } c)
            {
                return [];
            }

            var on = c.App.Get(AgentKeys.Enabled, false);
            return [new FeatureAction("toggle", (on ? "Turn off " : "Turn on ") + AgentStrings.Title, () => c.App.Set(AgentKeys.Enabled, !on), "")];
        }
    }
}
