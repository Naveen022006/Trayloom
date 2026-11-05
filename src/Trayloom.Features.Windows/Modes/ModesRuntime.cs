// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The Modes service (spec 08 section 2.4): the saved modes, the active one, switching and the events the surfaces listen to. Everything
/// here runs on the UI thread, and <see cref="Changed"/> is raised on it. The slow part of a switch runs on its own thread
/// (<see cref="ModesSwitcher"/>), so a slow app can never freeze Trayloom. Logs carry mode ids and counts only: never names, paths,
/// addresses, profile names or titles.
/// </summary>
internal sealed partial class ModesRuntime : IAwayWorkspace
{
    private readonly IFeatureContext _context;
    private readonly ModesStore _store;
    private readonly ModesTracking _tracking = new();
    private readonly ModesSwitcher _switcher;
    private readonly ModesFile _file;
    private Thread? _thread;
    private (string? ModeId, string Trigger)? _pending;
    private string? _activeId;
    private string? _switchingTo;
    private string? _notice;
    private bool _switching;
    private bool _keepAwakeByMode;
    private bool _stopped;

    public ModesRuntime(IFeatureContext context, ModesStore store)
    {
        _context = context;
        _store = store;
        _file = store.Load();
        _switcher = new ModesSwitcher(context.App, context.Log, context.Ui, _tracking, ApplyEnterActions);
        Elevated = AppIdentity.IsAdmin();

        // The active mode survives a restart (its windows are not tracked any more, which leaving and re-entering handle).
        var saved = context.App.Get<string>(ModesKeys.ActiveId);
        _activeId = _file.Modes.Any(m => m.Id == saved) ? saved : null;
        if (_activeId is null && !string.IsNullOrEmpty(saved))
        {
            context.App.Set(ModesKeys.ActiveId, string.Empty);
        }
    }

    // ------------------------------------------------------------------------------------------ state

    /// <summary>The saved modes in order, as copies: changing one changes nothing until <see cref="Update"/>.</summary>
    public IReadOnlyList<Mode> Modes => [.. _file.Modes.Select(m => m.Clone())];

    /// <summary>The mode the person is in, or null.</summary>
    public Mode? Active => _file.Modes.FirstOrDefault(m => m.Id == _activeId)?.Clone();

    public bool IsSwitching => _switching;

    public string? SwitchingToId => _switchingTo;

    public ModeSwitchSummary? LastSummary { get; private set; }

    /// <summary>True when Trayloom runs as administrator: the apps it starts would then open as administrator too, so Modes refuses.</summary>
    public bool Elevated { get; }

    /// <summary>True while one more mode can be added (a limit of twelve).</summary>
    public bool CanAdd => _file.Modes.Count < ModesMath.MaxModes;

    /// <summary>The island page or panel row that shows the state asks to be redrawn. Raised on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>Raised after a mode was added, edited, deleted or moved, so the shortcut roles can follow. UI thread.</summary>
    public event Action? ModesEdited;

    /// <summary>The island context the notice strip captured, so a finished switch can ask the island to show its notice.</summary>
    public IIslandContext? IslandContext { get; set; }

    /// <summary>The shortcut role of a mode (<c>modes.&lt;id&gt;</c>).</summary>
    public static string RoleId(string modeId) => "modes." + modeId;

    // ------------------------------------------------------------------------------------------ entering and leaving

    /// <summary>
    /// Starts a mode. Refuses with a message while Away mode is on, while Trayloom is elevated, when Modes is off, or when the mode is
    /// missing or empty. While a switch is running the request waits and the last one wins. True when the switch started or was queued.
    /// Triggers: tile, panel, quick-panel, command-bar, shortcut, settings.
    /// </summary>
    public bool Enter(string modeId, string trigger)
    {
        if (_stopped)
        {
            return false;
        }

        if (!_context.App.Get<bool>(ModesKeys.Enabled))
        {
            return Refuse(ModesStrings.RefusedOff);
        }

        var mode = _file.Modes.FirstOrDefault(m => m.Id == modeId);
        if (mode is null)
        {
            return Refuse(ModesStrings.RefusedMissing);
        }

        if (_context.Services.Get<AwayController>()?.IsAway == true)
        {
            return Refuse(ModesStrings.RefusedAway);
        }

        if (Elevated)
        {
            return Refuse(ModesStrings.RefusedElevated);
        }

        if (mode.Items.Count == 0)
        {
            return Refuse(ModesStrings.RefusedEmpty);
        }

        if (_switching)
        {
            _pending = (modeId, trigger);
            return true;
        }

        StartSwitch(mode.Clone(), trigger);
        return true;
    }

    /// <summary>Leaves the active mode: its windows are put away as its "when I leave" choice says, and no mode is active afterwards.</summary>
    public bool Leave(string trigger)
    {
        if (_stopped || _activeId is null)
        {
            return false;
        }

        if (_switching)
        {
            _pending = (null, trigger);
            return true;
        }

        StartSwitch(null, trigger);
        return true;
    }

    private bool Refuse(string message)
    {
        _context.Notifier.Show(ModesStrings.Title, message, NotificationLevel.Warning);
        return false;
    }

    private void StartSwitch(Mode? target, string trigger)
    {
        var previous = Active;
        _switching = true;
        _switchingTo = target?.Id;
        Changed?.Invoke();
        var thread = new Thread(() =>
        {
            ModeSwitchSummary summary;
            try
            {
                summary = _switcher.Execute(previous, target);
            }
            catch (Exception ex)
            {
                _context.Log.Error("[modes] switch failed", ex);
                summary = new ModeSwitchSummary(target?.Id ?? previous?.Id ?? string.Empty, 0, 0, 1, [(string.Empty, ModesStrings.ReasonFailed)], 0);
            }

            _context.Ui.BeginInvoke(() => Finish(target, previous, summary, trigger));
        })
        {
            IsBackground = true,
            Name = "Modes switch",
        };

        // Shell COM (File Explorer windows) needs a single-threaded apartment.
        thread.SetApartmentState(ApartmentState.STA);
        _thread = thread;
        thread.Start();
    }

    /// <summary>The part of a switch that must run on the UI thread, after the previous mode is put away: keep awake and the island timer.</summary>
    private void ApplyEnterActions(Mode? previous, Mode? target)
    {
        if (_stopped || target is null)
        {
            return;
        }

        var keepAwake = _context.Services.Get<IKeepAwakeService>();
        if (_keepAwakeByMode && target.OnEnter.KeepAwake == "off")
        {
            if (keepAwake?.IsActive == true)
            {
                keepAwake.Deactivate();
            }

            _keepAwakeByMode = false;
        }

        TimeSpan? duration = target.OnEnter.KeepAwake switch
        {
            "60" => TimeSpan.FromMinutes(60),
            "120" => TimeSpan.FromMinutes(120),
            _ => null,
        };
        if (keepAwake is not null && target.OnEnter.KeepAwake is "60" or "120" or "on")
        {
            keepAwake.Activate(duration);
            _keepAwakeByMode = true;
        }

        if (target.OnEnter.Timer)
        {
            _context.Actions.All.FirstOrDefault(x => FeatureCatalog.Resolve(x.Feature.Id) == "notch" && x.Action.Id == "timer").Action?.Invoke();
        }
    }

    /// <summary>The end of a switch, on the UI thread.</summary>
    private void Finish(Mode? target, Mode? previous, ModeSwitchSummary summary, string trigger)
    {
        _thread = null;
        _switching = false;
        _switchingTo = null;
        if (_stopped)
        {
            return;
        }

        LastSummary = summary;
        if (target is null)
        {
            _activeId = null;
            _context.App.Set(ModesKeys.ActiveId, string.Empty);
            DeactivateKeepAwake();
            _context.Log.Info($"[modes] leave {previous?.Id ?? "-"} ({trigger}): {summary.Prompts} asked something, in {summary.Seconds:0.0} s");
        }
        else
        {
            var stillThere = _file.Modes.Any(m => m.Id == target.Id);
            _activeId = stillThere ? target.Id : null;
            _context.App.Set(ModesKeys.ActiveId, _activeId ?? string.Empty);
            _context.Log.Info($"[modes] enter {target.Id} ({trigger}): {target.Items.Count} items, opened {summary.Opened}, focused {summary.Focused}, failed {summary.Failed}, in {summary.Seconds:0.0} s");
            if (stillThere && (trigger != "tile" || summary.Failed > 0))
            {
                Announce(summary.Failed > 0 ? ModesStrings.DidntOpen(target.Name, summary.Failed) : ModesStrings.Ready(target.Name));
            }
        }

        Changed?.Invoke();
        if (_pending is { } next)
        {
            _pending = null;
            if (next.ModeId is null)
            {
                Leave(next.Trigger);
            }
            else
            {
                Enter(next.ModeId, next.Trigger);
            }
        }
    }

    private void DeactivateKeepAwake()
    {
        if (!_keepAwakeByMode)
        {
            return;
        }

        _keepAwakeByMode = false;
        var keepAwake = _context.Services.Get<IKeepAwakeService>();
        if (keepAwake?.IsActive == true)
        {
            keepAwake.Deactivate();
        }
    }

    // ------------------------------------------------------------------------------------------ the notice

    /// <summary>The text of the notice a finished switch queued, once; null when there is none.</summary>
    public string? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }

    /// <summary>Shows the result on the island when it is presenting, and as a toast when it is not.</summary>
    private void Announce(string text)
    {
        var island = IslandContext;
        var presenting = _context.Runtime.IsRunning("notch") && _context.App.Get<bool>("notchEnabled") && island is not null;
        if (presenting)
        {
            _notice = text;
            island!.Invalidate();
        }
        else
        {
            _context.Notifier.Show(ModesStrings.Title, text);
        }
    }

    // ------------------------------------------------------------------------------------------ editing

    /// <summary>
    /// Adds a mode (its id is made if it has none or the id is taken) and saves at once. Returns the mode as stored. When twelve modes
    /// exist already nothing is added and the mode comes back unchanged; check <see cref="CanAdd"/> first.
    /// </summary>
    public Mode Add(Mode mode)
    {
        if (!CanAdd)
        {
            _context.Log.Warn("[modes] add refused: the limit of modes is reached");
            return mode.Clone();
        }

        var copy = mode.Clone();
        if (string.IsNullOrWhiteSpace(copy.Id) || _file.Modes.Any(m => m.Id == copy.Id))
        {
            copy.Id = ModesMath.NewModeId();
        }

        var clean = Cleaned(copy);
        _file.Modes.Add(clean);
        Commit();
        return clean.Clone();
    }

    /// <summary>Replaces the saved mode that has the same id.</summary>
    public void Update(Mode mode)
    {
        var index = _file.Modes.FindIndex(m => m.Id == mode.Id);
        if (index < 0)
        {
            return;
        }

        _file.Modes[index] = Cleaned(mode.Clone());
        Commit();
    }

    /// <summary>
    /// Deletes a mode. Its apps and tabs stay as they are. When it was the active mode no mode is active afterwards, its shortcut goes
    /// away (the feature follows <see cref="ModesEdited"/>) and its tracked windows are forgotten.
    /// </summary>
    public void Delete(string modeId)
    {
        if (_file.Modes.RemoveAll(m => m.Id == modeId) == 0)
        {
            return;
        }

        _tracking.DropMode(modeId);
        if (_activeId == modeId)
        {
            _activeId = null;
            _context.App.Set(ModesKeys.ActiveId, string.Empty);
            DeactivateKeepAwake();
        }

        Commit();
    }

    /// <summary>Moves a mode up (negative) or down (positive) in the list.</summary>
    public void Move(string modeId, int delta)
    {
        var index = _file.Modes.FindIndex(m => m.Id == modeId);
        var target = Math.Clamp(index + delta, 0, _file.Modes.Count - 1);
        if (index < 0 || target == index)
        {
            return;
        }

        var mode = _file.Modes[index];
        _file.Modes.RemoveAt(index);
        _file.Modes.Insert(target, mode);
        Commit();
    }

    /// <summary>The mode run through the same rules as a loaded file (limits, kinds, colours), so what the editor saves can never break the file.</summary>
    private static Mode Cleaned(Mode mode)
    {
        var clean = ModesMath.Sanitize(new ModesFile { Modes = [mode] });
        return clean.Modes.Count > 0 ? clean.Modes[0] : mode;
    }

    private void Commit()
    {
        _store.Save(_file);
        Changed?.Invoke();
        ModesEdited?.Invoke();
    }

    // ------------------------------------------------------------------------------------------ capture and browsers

    /// <summary>Reads what is open now into memory (a single-threaded apartment thread). Nothing is saved.</summary>
    public Task<ModeCapture> CaptureAsync()
    {
        var source = new TaskCompletionSource<ModeCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                source.SetResult(ModesCapture.Run(_context.App, _context.Log, _context.Ui));
            }
            catch (Exception ex)
            {
                source.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Modes capture",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return source.Task;
    }

    /// <summary>Reads the open tabs of Chrome or Edge for one profile from the browser's session file. Only when the person asks.</summary>
    public Task<BrowserTabsResult> ReadBrowserTabsAsync(string browserId, string profile) =>
        Task.Run(() => ModesBrowsers.ReadTabs(browserId, profile));

    /// <summary>The default browser and the installed ones.</summary>
    public IReadOnlyList<BrowserChoice> Browsers() => ModesBrowsers.Choices();

    /// <summary>Profile folder and display name of each profile of a Chromium browser.</summary>
    public IReadOnlyList<(string Dir, string Name)> ChromiumProfiles(string browserId) => ModesBrowsers.ChromiumProfiles(browserId);

    // ------------------------------------------------------------------------------------------ lifetime and dev

    /// <summary>Stops the switch thread (it gets three seconds), turns keep awake off if a mode turned it on, and forgets the windows. Never minimizes or closes anything.</summary>
    public void Stop()
    {
        _stopped = true;
        _switcher.Cancel();
        _thread?.Join(3000);
        DeactivateKeepAwake();
        _tracking.Clear();
        IslandContext = null;
    }

    /// <summary>Writes counts to the log (dev action); no names, paths or addresses.</summary>
    public void Dump()
    {
        var items = _file.Modes.Sum(m => m.Items.Count);
        _context.Log.Info($"[modes] dump: {_file.Modes.Count} modes, {items} items, active {(_activeId is null ? "none" : _activeId)}, tracked windows {_tracking.Count}, switching {_switching}, elevated {Elevated}");
    }
}
