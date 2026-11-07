// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Island context handed to contributed sections / strips of other projects.</summary>
internal sealed class IslandCtx(IContributionServices services, IslandCore core) : ContributionServicesBase(services), IIslandContext
{
    public void Invalidate() => core.InvalidateExternal();

    public IDisposable? HoldOpen() => core.HoldOpen();

    public void EnsureKeyboard()
    {
        if (core.Window is not { } window)
        {
            return;
        }

        window.EnsureKeyboard();

        // A click on a box may arrive while another window still holds the foreground; ask for it back so the first key types.
        if (!window.IsForeground)
        {
            window.ReclaimKeyboard();
        }
    }

    public void ReleaseKeyboard() => core.Window?.ReleaseKeyboard();

    public void Relayout() => core.Window?.BuildOpen(rebuildPage: false);
}

/// <summary>A module as the island shows it (built-in or contributed).</summary>
internal sealed record ModuleInfo(string Id, string Title, string Glyph, Color Tint, bool DarkGlyph, char Key, string Summary, bool Builtin);

/// <summary>One notice at a time with the replacement rule of spec 7.7.</summary>
internal sealed class NoticeCenter
{
    private readonly DispatcherTimer _timer;
    private bool _held;

    public NoticeCenter(Dispatcher ui)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal, ui);
        _timer.Tick += (_, _) => Clear();
    }

    public Notice? Current { get; private set; }

    public bool Held => _held;

    public event Action? Changed;

    public bool Post(Notice notice)
    {
        if (Current is { } cur)
        {
            if (_held ? notice.Event.Priority <= cur.Event.Priority && notice.Event.Id != cur.Event.Id : notice.Event.Priority < cur.Event.Priority)
            {
                return false;
            }
        }

        Current = notice;
        _held = false;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(notice.Event.Seconds);
        _timer.Start();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Pointer rests on the notice: the timer stops (banner hold); false restarts the full duration.</summary>
    public void Hold(bool held)
    {
        if (Current is null)
        {
            return;
        }

        _held = held;
        _timer.Stop();
        if (!held)
        {
            _timer.Interval = TimeSpan.FromSeconds(Current.Event.Seconds);
            _timer.Start();
        }
    }

    public void Clear()
    {
        _timer.Stop();
        _held = false;
        if (Current is not null)
        {
            Current = null;
            Changed?.Invoke();
        }
    }
}

/// <summary>Everything the island needs: services, settings, notices, activities and actions. One instance while the feature runs.</summary>
internal sealed class IslandCore : IDisposable
{
    public static IslandCore? Current { get; private set; }

    private readonly List<IDisposable> _disposables = [];
    private bool _disposed;

    public IslandCore(IFeatureContext ctx)
    {
        Ctx = ctx;
        K.RegisterDefaults(ctx.App);
        Ui = ctx.Ui;
        IslandContext = new IslandCtx(ctx, this);
        Notices = new NoticeCenter(Ui);
        Playback = new PlaybackService(Ui, ctx.Log);
        Audio = new AudioService(Ui);
        Levels = new AudioLevelService();
        Timer = new TimerService(ctx.App, Ui, ctx.Log);
        Focus = new FocusService(this);
        Calendar = new CalendarService(ctx.App, Ui, ctx.Log);
        Meeting = new MeetingService(this);
        Session = new SessionState(Ui);
        Clipboard = new ClipboardSource(this);
        Downloads = new DownloadsService(this);
        Notifications = new NotificationsService(this);
        Watch = new WatchService(this);
        Accessories = new AccessoryService(this);
        Mascot = new MascotService(this);
        Lyrics = new LyricsService(this);
        AppleMusic = new AppleMusicService(this);
        Music = new MusicServices(this);
        MicUse = new MicUseService(Ui, ctx.Log);
        Sources = new NoticeSources(this);
        Current = this;
    }

    public IFeatureContext Ctx { get; }

    public IAppSettings App => Ctx.App;

    public Dispatcher Ui { get; }

    public IslandCtx IslandContext { get; }

    public NoticeCenter Notices { get; }

    public PlaybackService Playback { get; }

    public AudioService Audio { get; }

    public AudioLevelService Levels { get; }

    public TimerService Timer { get; }

    /// <summary>The Focus session: a Pomodoro that can enter a mode and hold back optional notices (spec 10c).</summary>
    public FocusService Focus { get; }

    public CalendarService Calendar { get; }

    /// <summary>The next-meeting heads-up and its microphone check (spec 10d).</summary>
    public MeetingService Meeting { get; }

    public SessionState Session { get; }

    public ClipboardSource Clipboard { get; }

    public DownloadsService Downloads { get; }

    public NotificationsService Notifications { get; }

    public WatchService Watch { get; }

    public AccessoryService Accessories { get; }

    public MascotService Mascot { get; }

    public LyricsService Lyrics { get; }

    public AppleMusicService AppleMusic { get; }

    /// <summary>Spotify and YouTube Music (off until the user turns them on and sets them up in Settings).</summary>
    public MusicServices Music { get; }

    public NoticeSources Sources { get; }

    /// <summary>Apps capturing from the microphone (runs while "Show when an app uses the microphone" is on).</summary>
    public MicUseService MicUse { get; }

    /// <summary>The indicator is on and at least one app is capturing.</summary>
    public bool MicInUse => MicUse.Active && App.Get<bool>(K.MicInUse);

    public IslandWindow? Window { get; set; }

    public event Action? ExternalInvalidated;

    public event Action? ModulesChanged;

    public void InvalidateExternal() => Ui.BeginInvoke(() => ExternalInvalidated?.Invoke());

    public void RaiseModulesChanged() => ModulesChanged?.Invoke();

    public bool IslandOn => App.Get<bool>(K.Enabled);

    public bool Reduced => D.ReduceMotion;

    public void Start()
    {
        Playback.IncludeOthers = () => App.Get<bool>(K.IncludeOtherPlayers);
        Playback.Start();
        Clipboard.Start();
        Downloads.Start();
        Notifications.Start();
        Watch.Start();
        Accessories.Start();
        Mascot.Start();
        Session.Start();
        Sources.Start();
        Meeting.Start();
        MicUse.Changed += OnMicUse;
        SyncMicUse();
        Ctx.Runtime.Changed += OnRuntime;
        Ctx.Island.Changed += OnRuntime;
        Ctx.Services.Changed += OnServices;
        App.Changed += OnSetting;
        EqBars.LiveSource = () => Levels.Levels;
        Playback.Changed += SyncLevels;
        SyncLevels();
        StartSpotifyTimeline();
        var dir = Ctx.DataDirectory;
        _ = dir;
    }

    /// <summary>
    /// A feature published or withdrew a service. The island starts before the features that contribute strips (the Agents runtime is
    /// provided a moment after the first closed render), and a contribution that asked for its service too early has not seen the island's
    /// context yet and cannot invalidate: so the island asks again itself.
    /// </summary>
    private void OnServices() => InvalidateExternal();

    private void OnRuntime() => Ui.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }

        // installing or removing a feature changes which strips exist and what they answer
        ResetStrips();
        ExternalInvalidated?.Invoke();
        ModulesChanged?.Invoke();
        Downloads.Sync();
        Notifications.Sync();
        Accessories.Sync();
        Watch.Sync();
        Mascot.Sync();
    });

    private void OnSetting(string key)
    {
        if (key.StartsWith("notch", StringComparison.Ordinal) || key.StartsWith("mixer", StringComparison.Ordinal) || key.StartsWith("scratchpad", StringComparison.Ordinal))
        {
            Ui.BeginInvoke(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (key is K.Enabled or K.HiddenModules or K.ModuleOrder or K.CalendarEnabled or K.TimerEnabled or K.AgentsEnabled or K.WatchEnabled or K.NotificationsEnabled or K.CameraEnabled)
                {
                    ModulesChanged?.Invoke();
                }

                if (key is K.NotificationsEnabled or K.DismissNative or K.NotificationsReadScreen)
                {
                    Notifications.Sync();
                }

                if (key is K.MicInUse)
                {
                    SyncMicUse();
                }

                if (key is K.Accessories)
                {
                    Accessories.Sync();
                }

                if (key is K.WatchEnabled)
                {
                    Watch.Sync();
                }

                if (key is K.DownloadsEnabled or K.DownloadsFolder)
                {
                    Downloads.Sync();
                }

                if (key is K.LiveEqualizer)
                {
                    SyncLevels();
                }

                if (key.StartsWith("notchMascot", StringComparison.Ordinal))
                {
                    Mascot.Sync();
                }
            });
        }
    }

    /// <summary>Starts or stops the microphone watch with its setting and redraws the closed island.</summary>
    private void SyncMicUse()
    {
        if (App.Get<bool>(K.MicInUse))
        {
            MicUse.Start();
        }
        else
        {
            MicUse.Stop();
        }

        OnMicUse();
    }

    private void OnMicUse()
    {
        if (!_disposed)
        {
            Window?.RefreshClosed();
            if (Window is { Expanded: true })
            {
                MicUseChanged?.Invoke();
            }
        }
    }

    /// <summary>The set of apps using the microphone changed while the island is open (pages that show it rebuild).</summary>
    public event Action? MicUseChanged;

    private void SyncLevels()
    {
        var p = Playback.Current;
        var want = App.Get<bool>(K.LiveEqualizer) && Ctx.Runtime.IsAvailable("notch-live-equalizer") && p is { IsPlaying: true } && !D.ReduceMotion && Session.CanPresent;
        if (want)
        {
            Levels.Start(p.Identity);
        }
        else if (Levels.IsRunning)
        {
            Levels.Stop();
        }
    }

    // ---------------------------------------------------------------- Spotify timeline

    private bool _watchingSpotify;

    /// <summary>Hooks the Web API timeline into the media session's reading. Spotify's own session often has no length or seeking.</summary>
    private void StartSpotifyTimeline()
    {
        Playback.TimelineFor = SpotifyTimelineFor;
        Playback.Changed += SyncSpotifyTimeline;
        Music.Changed += SyncSpotifyTimeline;
        Music.Spotify.TimelineChanged += OnSpotifyTimeline;
        SyncSpotifyTimeline();
    }

    private ExternalTimeline? SpotifyTimelineFor(string aumid) =>
        MusicSources.FromPlayer(aumid) == MusicSource.Spotify && Music.Spotify.Connection == StreamConnection.Connected ? Music.Spotify.Timeline : null;

    private bool SpotifySeek(string aumid, double seconds) =>
        MusicSources.FromPlayer(aumid) == MusicSource.Spotify && Music.Spotify.TrySeek(seconds);

    private void OnSpotifyTimeline()
    {
        if (!_disposed)
        {
            Playback.Reevaluate();
        }
    }

    /// <summary>
    /// Polls the Web API only while it is needed: the island is open, the player is Spotify, Spotify is connected and the session reports no
    /// length. Anything else (closing, another player, disconnecting, a session with its own timeline) stops the polling at once. Also decides
    /// whether seeking through the Web API is on offer (not for an account known to be free).
    /// </summary>
    public void SyncSpotifyTimeline()
    {
        var spotify = Music.Spotify;
        var current = Playback.Current;
        var connected = !_disposed && spotify.Connection == StreamConnection.Connected;
        var want = connected
            && Window is { Expanded: true }
            && current.HasTrack
            && MusicSources.FromPlayer(current.Aumid) == MusicSource.Spotify
            && current.SessionDuration <= 0;
        if (want != _watchingSpotify)
        {
            _watchingSpotify = want;
            spotify.WatchTimeline(want);
        }

        Func<string, double, bool>? seek = connected && spotify.CanPlayHere != false ? SpotifySeek : null;
        if ((seek is null) != (Playback.ExternalSeek is null))
        {
            Playback.ExternalSeek = seek;
            Playback.Reevaluate();
        }
    }

    // ---------------------------------------------------------------- modules

    public IIslandSection? External(string id) => Ctx.Island.Sections.FirstOrDefault(s => s.Id == id);

    public IReadOnlyList<ModuleInfo> AllModules()
    {
        var list = new List<ModuleInfo>();
        foreach (var id in IslandCatalog.OrderedIds(App, Ctx.Island.Sections.Select(s => s.Id)))
        {
            var def = IslandCatalog.Get(id);
            var ext = External(id);
            if (def is not null)
            {
                list.Add(new ModuleInfo(id, ext?.Title ?? def.Title, string.IsNullOrEmpty(ext?.Glyph) ? def.Glyph : ext!.Glyph, def.Tint, def.DarkGlyph, def.Key, def.Summary, true));
            }
            else if (ext is not null)
            {
                list.Add(new ModuleInfo(id, ext.Title, ext.Glyph, Color.FromRgb(0x98, 0x98, 0x9D), false, '\0', ext.Title, false));
            }
        }

        return list;
    }

    public bool IsHidden(string id) => K.Csv(App.Get<string>(K.HiddenModules)).Contains(id);

    /// <summary>The feature behind the module is installed (and for contributed pages, the page exists).</summary>
    public bool IsModuleInstalled(string id)
    {
        var def = IslandCatalog.Get(id);
        if (def is null)
        {
            return External(id) is not null;
        }

        if (!IslandCatalog.FeatureAvailable(Ctx.Runtime, def))
        {
            return false;
        }

        return !IslandCatalog.IsContributed(id) || External(id) is not null;
    }

    public bool IsModuleEnabledKey(string id)
    {
        var def = IslandCatalog.Get(id);
        return def?.EnableKey is null || App.Get<bool>(def.EnableKey);
    }

    /// <summary>The sections the island shows (stored order, minus hidden and unavailable).</summary>
    public List<ModuleInfo> Modules() => [.. AllModules().Where(m => !IsHidden(m.Id) && IsModuleInstalled(m.Id) && IsModuleEnabledKey(m.Id) && (m.Id != "tools" || true))];

    public bool ModuleVisible(string id) => Modules().Any(m => m.Id == id);

    // ---------------------------------------------------------------- activities

    public const string ActTimer = "timer";
    public const string ActWatch = "watch";
    public const string ActDownloads = "downloads";
    public const string ActAgents = "agents";
    public const string ActMicrophone = "microphone";
    public const string ActCalendar = "calendar";
    public const string ActMeeting = "meeting";
    public const string ActMusic = "music";
    public const string ActKeepAwake = "keepAwake";

    private string? _picked;

    public IKeepAwakeService? KeepAwake => Ctx.Services.Get<IKeepAwakeService>();

    /// <summary>Live closed-island activities in priority order (spec 3.2). External strips come after keep awake except agents.</summary>
    public List<string> LiveActivities()
    {
        var list = new List<string>();
        if (!Session.CanPresent)
        {
            return list;
        }

        if (Timer.HasSession && Ctx.Runtime.IsAvailable("notch-timer") && App.Get<bool>(K.TimerEnabled) && (!App.Get<bool>(K.HideTimerCountdown) || Timer.Completed))
        {
            list.Add(ActTimer);
        }

        if (Watch.Active && Ctx.Runtime.IsAvailable("notch-watch") && App.Get<bool>(K.WatchEnabled))
        {
            list.Add(ActWatch);
        }

        if (Downloads.HasActive && Ctx.Runtime.IsAvailable("notch-downloads") && App.Get<bool>(K.DownloadsEnabled))
        {
            list.Add(ActDownloads);
        }

        // Agents rank above music only while one needs the person; a working or resting agent lets a playing song keep its banner and
        // follows right after it, so both stay one click (or swipe) apart in the picker.
        var agentsLive = Ctx.Runtime.IsAvailable("notch-agents") && App.Get<bool>(K.AgentsEnabled) && App.Get<bool>(K.AgentsLiveActivity) && ExternalStrip("agents") is not null;
        var agentsBeforeMusic = agentsLive && (!ShowsMusicActivity || (Ctx.Island.Strips.FirstOrDefault(s => s.Id == "agents")?.OutranksMusic(IslandContext) ?? true));
        if (agentsBeforeMusic)
        {
            list.Add(ActAgents);
        }

        // A meeting a few minutes away is more urgent than a song or a countdown, but less than an agent that is blocked waiting for you.
        if (Meeting.Current is not null)
        {
            list.Add(ActMeeting);
        }

        // A privacy indicator outranks what is merely nice to see (calendar countdown, music, keep awake) and ranks just below the
        // activities the person started and watches (timer, watch, downloads, agents); those get an orange dot while it is live.
        if (MicInUse)
        {
            list.Add(ActMicrophone);
        }

        if (Ctx.Runtime.IsAvailable("notch-calendar") && App.Get<bool>(K.CalendarEnabled)
            && Calendar.CountdownTarget(DateTime.Now) is { } countdown && countdown.Event.CountdownKey != Meeting.Current?.CountdownKey)
        {
            list.Add(ActCalendar);
        }

        if (ShowsMusicActivity)
        {
            list.Add(ActMusic);
        }

        if (agentsLive && !agentsBeforeMusic)
        {
            list.Add(ActAgents);
        }

        if (App.Get<bool>(K.KeepAwakeActivity) && Ctx.Runtime.IsAvailable("keep-awake") && KeepAwake is { IsActive: true })
        {
            list.Add(ActKeepAwake);
        }

        foreach (var s in Ctx.Island.Strips)
        {
            if (s.Id is "agents" or "agents.resting" || s.Id.EndsWith(".resting", StringComparison.Ordinal) || s.Id.EndsWith(".notice", StringComparison.Ordinal) || s.Id.EndsWith(".attention", StringComparison.Ordinal) || list.Contains(s.Id))
            {
                continue;
            }

            if (ExternalStrip(s.Id) is not null)
            {
                list.Add(s.Id);
            }
        }

        if (_picked is not null && !list.Contains(_picked))
        {
            _picked = null;
        }

        return list;
    }

    /// <summary>
    /// A playing song is an activity, not resting content: "Show music while playing" alone decides whether it takes the closed island.
    /// The "At rest" choice (even "Nothing") only says what shows when nothing is going on, so it no longer hides a playing song.
    /// </summary>
    public bool ShowsMusicActivity =>
        Playback.Current is { IsPlaying: true }
        && App.Get<bool>(K.ShowPlayingMusic)
        && ModuleVisible("music");

    public string? PickedActivity
    {
        get => _picked;
        set => _picked = value;
    }

    /// <summary>The activity the closed island shows: the user's pick or the first live one.</summary>
    public string? CurrentActivity()
    {
        var live = LiveActivities();

        // A ringing alarm outranks the person's pick: it has to be seen and stopped.
        if (Timer.Ringing && live.Contains(ActTimer))
        {
            return ActTimer;
        }

        if (_picked is not null && live.Contains(_picked))
        {
            return _picked;
        }

        return live.Count > 0 ? live[0] : null;
    }

    private readonly Dictionary<string, FrameworkElement?> _strips = [];

    /// <summary>
    /// The strip element of a contributed island strip, cached while it exists (null = nothing to show). A "nothing" answer is never
    /// cached: the contribution is asked again on the next read, so an answer given before its feature finished starting (or before a
    /// session began) cannot hide the strip until some unrelated refresh happens.
    /// </summary>
    public FrameworkElement? ExternalStrip(string id)
    {
        var strip = Ctx.Island.Strips.FirstOrDefault(s => s.Id == id);
        if (strip is null)
        {
            _strips.Remove(id);
            return null;
        }

        if (_strips.TryGetValue(id, out var cached) && cached is not null)
        {
            return cached;
        }

        FrameworkElement? el;
        try
        {
            el = strip.CreateStrip(IslandContext);
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn($"Island strip {id} failed: {ex.Message}");
            el = null;
        }

        if (el is not null)
        {
            _strips[id] = el;
        }
        else
        {
            _strips.Remove(id);
        }

        return el;
    }

    /// <summary>Drops the cached strips so the next read re-queries the providers (called by Invalidate and registry changes).</summary>
    public void ResetStrips() => _strips.Clear();

    private DateTime _lastExternalNotice = DateTime.MinValue;

    /// <summary>Contributed strips whose id ends with ".notice" are shown as one 5 s notice each time they produce an element.</summary>
    public void PollExternalNotices()
    {
        if (!IslandOn || !Session.CanPresent)
        {
            return;
        }

        // A contributed ".attention" strip (an agent waits for the person) shows at once, with a click that runs the element's Tag action.
        foreach (var s in Ctx.Island.Strips.Where(x => x.Id.EndsWith(".attention", StringComparison.Ordinal)).ToList())
        {
            var attention = TakeStrip(s.Id);
            _strips.Remove(s.Id);
            if (attention is null)
            {
                continue;
            }

            ShowNotice(new Notice { Event = NoticeEvents.AgentsAttention, Element = attention, Glyph = G.Claude, Title = "attention", Detail = Guid.NewGuid().ToString("N"), OnClick = attention.Tag as Action, Banner = false });
        }

        foreach (var s in Ctx.Island.Strips.Where(x => x.Id.EndsWith(".notice", StringComparison.Ordinal)).ToList())
        {
            if ((DateTime.UtcNow - _lastExternalNotice).TotalSeconds < 7)
            {
                return;
            }

            var el = TakeStrip(s.Id);
            _strips.Remove(s.Id);
            if (el is null)
            {
                continue;
            }

            _lastExternalNotice = DateTime.UtcNow;
            // AI Agents keeps its own event (its switch and page); any other "<owner>.notice" opens the owner's page when the island has one.
            var owner = s.Id[..^".notice".Length];
            if (owner == "agents")
            {
                ShowNotice(new Notice { Event = NoticeEvents.Agents, Element = el, Glyph = G.Claude, Title = "" });
            }
            else
            {
                ShowNotice(new Notice { Event = NoticeEvents.External, Element = el, Glyph = G.Bell, Title = "", Detail = s.Id + ":" + Environment.TickCount64, OpensModule = IslandCatalog.Get(owner) is not null || External(owner) is not null ? owner : null });
            }
        }
    }

    /// <summary>Takes a cached strip out of its parent so it can be placed in a new tree.</summary>
    public FrameworkElement? TakeStrip(string id)
    {
        var el = ExternalStrip(id);
        if (el is null)
        {
            return null;
        }

        if (el.Parent is System.Windows.Controls.Panel panel)
        {
            panel.Children.Remove(el);
        }
        else if (el.Parent is System.Windows.Controls.Decorator dec)
        {
            dec.Child = null;
        }
        else if (el.Parent is System.Windows.Controls.ContentControl cc)
        {
            cc.Content = null;
        }

        return el;
    }

    // ---------------------------------------------------------------- actions

    /// <summary>Runs the first FeatureAction of a running feature whose id contains one of the hints (or the first action).</summary>
    public bool InvokeFeatureAction(string catalogId, params string[] hints)
    {
        var all = Ctx.Actions.All.Where(a => a.Feature.CatalogId == catalogId || a.Feature.Id == catalogId).ToList();
        if (all.Count == 0)
        {
            return false;
        }

        var pick = hints.Length == 0
            ? all[0]
            : all.FirstOrDefault(a => hints.Any(h => a.Action.Id.Contains(h, StringComparison.OrdinalIgnoreCase) || a.Action.Title.Contains(h, StringComparison.OrdinalIgnoreCase)));
        if (pick.Action is null)
        {
            pick = all[0];
        }

        try
        {
            pick.Action.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn($"Island action failed: {ex.Message}");
            return false;
        }
    }

    public IDisposable? HoldOpen() => Window?.HoldOpen();

    public void Open(string? module = null, bool pinned = false, bool takeFocus = true) => Window?.Open(module, pinned, takeFocus);

    public void Collapse() => Window?.Collapse();

    /// <summary>Closes the island (or hides it) and runs the action once the screen is free.</summary>
    public void Perform(Action action)
    {
        Window?.Collapse();
        Ui.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            await Task.Delay(180);
            action();
        });
    }

    public void ShowNotice(Notice notice)
    {
        if (!Session.CanPresent || !IslandOn || (notice.Event != NoticeEvents.Unlock && !App.Get<bool>(notice.Event.PrefKey)))
        {
            return;
        }

        if (Window is { Expanded: true } || (Window?.CaptureUp ?? false))
        {
            return;
        }

        // A focus session holds the optional notices back; they stay on the Notifications page and a summary follows the focus phase.
        if (Focus.Hold(notice))
        {
            return;
        }

        Notices.Post(notice);
    }

    public void Dispose()
    {
        _disposed = true;
        Playback.Changed -= SyncSpotifyTimeline;
        Music.Changed -= SyncSpotifyTimeline;
        Music.Spotify.TimelineChanged -= OnSpotifyTimeline;
        Playback.TimelineFor = null;
        Playback.ExternalSeek = null;
        _watchingSpotify = false;
        Music.Spotify.WatchTimeline(false);
        Ctx.Runtime.Changed -= OnRuntime;
        Ctx.Island.Changed -= OnRuntime;
        Ctx.Services.Changed -= OnServices;
        App.Changed -= OnSetting;
        Playback.Changed -= SyncLevels;
        EqBars.LiveSource = null;
        Levels.Dispose();
        Playback.Dispose();
        AppleMusic.Dispose();
        Music.Dispose();
        Audio.Dispose();
        Focus.Dispose();
        Timer.Dispose();
        Meeting.Dispose();
        Calendar.Dispose();
        Session.Dispose();
        Clipboard.Dispose();
        Downloads.Dispose();
        Notifications.Dispose();
        Watch.Dispose();
        Accessories.Dispose();
        Mascot.Dispose();
        MicUse.Changed -= OnMicUse;
        MicUse.Dispose();
        Sources.Dispose();
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }
}
