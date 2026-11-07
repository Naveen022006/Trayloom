// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// Runs the rules of <see cref="ModesAutoMath"/>: an app rule fires when the app's window first comes to the front, a time rule when its
/// time has come. Windows has no event for "a program started" that works without administrator rights, so the foreground WinEvent
/// hook is used, exactly as Screen Time does. A rule never replaces a mode the person is in, never leaves a mode it did not start, and
/// stays silent while Away mode is on, the session is locked or Trayloom runs elevated. Everything runs on the UI thread. Logs carry
/// rule and mode ids only, never executable names.
/// </summary>
internal sealed class ModesAutoRunner : IDisposable
{
    private static readonly TimeSpan PendingLife = TimeSpan.FromMinutes(2);

    private readonly IFeatureContext _context;
    private readonly ModesRuntime _runtime;
    private readonly ModesRulesStore _store;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private ModeRulesFile _file = new();
    private WinApi.WinEventProc? _proc;
    private nint _hook;
    private (string RuleId, string ModeId, DateTime Expires)? _pending;
    private volatile bool _locked;
    private bool _running;
    private bool _stopped;

    public ModesAutoRunner(IFeatureContext context, ModesRuntime runtime, ModesRulesStore store)
    {
        _context = context;
        _runtime = runtime;
        _store = store;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The saved rules, as copies.</summary>
    public IReadOnlyList<ModeRule> Rules => [.. _file.Rules.Select(r => r.Clone())];

    /// <summary>Loads the rules and starts listening. The hook and the timer only run while a rule needs them.</summary>
    public void Start()
    {
        _file = _store.Load();
        _runtime.Changed += OnRuntimeChanged;
        _runtime.SuggestionAccepted += OnSuggestionAccepted;
        _context.App.Changed += OnSettingChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _running = true;
        Refresh();
        OnRuntimeChanged();
        _context.Log.Info($"[modes] auto started: {_file.Rules.Count} rules");
    }

    /// <summary>Unhooks, stops the timer and forgets everything. Starts nothing and changes no window.</summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _running = false;
        _runtime.Changed -= OnRuntimeChanged;
        _runtime.SuggestionAccepted -= OnSuggestionAccepted;
        _context.App.Changed -= OnSettingChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        RemoveHook();
        _timer.Stop();
        _seen.Clear();
        _pending = null;
        _context.Log.Info("[modes] auto stopped");
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------------------------------ editing

    /// <summary>Replaces the rules (sanitised) and saves them at once. False when the file could not be written.</summary>
    public bool Save(IEnumerable<ModeRule> rules)
    {
        var clean = ModesAutoMath.Sanitize(new ModeRulesFile { Rules = [.. rules] });
        var saved = _store.Save(clean);
        if (saved)
        {
            _file = clean;
            Refresh();
        }

        return saved;
    }

    // ------------------------------------------------------------------------------------------ what needs to run

    private bool Active => _running && _context.App.Get<bool>(ModesAutoKeys.Enabled) && _context.App.Get<bool>(ModesKeys.Enabled);

    /// <summary>Starts or stops the hook and the timer to match the rules and the switches.</summary>
    private void Refresh()
    {
        if (_stopped)
        {
            return;
        }

        var active = Active;
        if (active && _file.Rules.Any(r => r.Enabled && r.Kind == "app"))
        {
            SeedRunning();
            InstallHook();
        }
        else
        {
            RemoveHook();
        }

        if (active && _file.Rules.Any(r => r.Enabled && r.Kind == "time"))
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnSettingChanged(string key)
    {
        if (key is ModesAutoKeys.Enabled or ModesKeys.Enabled)
        {
            // Settings are raised on any thread; the hook and the timer belong to the UI thread.
            _context.Ui.BeginInvoke(() => Refresh());
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                _locked = true;
                break;
            case SessionSwitchReason.SessionUnlock:
                _locked = false;

                // A time rule that came due while the PC was locked still has its grace period.
                _context.Ui.BeginInvoke(() => Tick());
                break;
        }
    }

    // ------------------------------------------------------------------------------------------ app rules

    private void InstallHook()
    {
        if (_hook != 0)
        {
            return;
        }

        _proc = OnForeground;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_SYSTEM_FOREGROUND, WinApi.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT);
        if (_hook == 0)
        {
            _proc = null;
            _context.Log.Warn("[modes] auto foreground hook failed: app rules are off, time rules keep working");
        }
    }

    private void RemoveHook()
    {
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }

        _proc = null;
    }

    /// <summary>
    /// Marks the programs that are running already as seen, so restarting Trayloom with Code open does not fire its rule. A rule added
    /// later is seeded the same way when it is saved.
    /// </summary>
    private void SeedRunning()
    {
        foreach (var exe in _file.Rules.Where(r => r.Enabled && r.Kind == "app").Select(r => ModesAutoMath.NormalizeExe(r.App)).Distinct())
        {
            var processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
                foreach (var process in processes)
                {
                    if (ProcessFacts.Read(process.Id) is { } facts)
                    {
                        _seen.Add(SeenKey(exe, process.Id, facts.Created));
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _context.Log.Warn("[modes] auto could not list running programs: " + ex.GetType().Name);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    private static string SeenKey(string exe, int pid, long created) => exe + "|" + pid + "|" + created;

    /// <summary>The foreground hook callback. It arrives on the UI thread (out-of-context hook) and does one image-name query.</summary>
    private void OnForeground(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (_stopped || hwnd == 0)
        {
            return;
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == Environment.ProcessId || ProcessFacts.Read((int)pid) is not { } facts)
            {
                return;
            }

            var exe = ModesAutoMath.NormalizeExe(facts.Path);
            var matching = _file.Rules.Where(r => ModesAutoMath.AppMatches(r, exe)).ToList();
            if (matching.Count == 0 || !_seen.Add(SeenKey(exe, (int)pid, facts.Created)))
            {
                return;
            }

            var display = ProcessFacts.DisplayName(facts.Path);
            foreach (var rule in matching)
            {
                TryFire(rule, false, string.IsNullOrWhiteSpace(rule.AppName) ? display : rule.AppName);
            }
        }
        catch (Exception ex)
        {
            _context.Log.Warn("[modes] auto foreground event failed: " + ex.GetType().Name);
        }
    }

    // ------------------------------------------------------------------------------------------ time rules

    private void Tick()
    {
        if (!Active)
        {
            return;
        }

        var now = DateTime.Now;
        var fired = LoadFired();
        var changed = false;
        foreach (var rule in _file.Rules)
        {
            fired.TryGetValue(rule.Id, out var lastDay);
            if (ModesAutoMath.TimeDue(rule, now, lastDay))
            {
                // Not marked when the rule had to wait (locked, Away, a switch running): it tries again within its grace period.
                if (TryFire(rule, true, ModesAutoStrings.TimeText(rule.At)))
                {
                    fired[rule.Id] = ModesAutoMath.DayKey(now);
                    changed = true;
                }
            }
            else if (ModesAutoMath.UntilDue(rule, now))
            {
                TryLeave(rule);
            }
        }

        if (changed)
        {
            SaveFired(fired);
        }
    }

    private Dictionary<string, string> LoadFired()
    {
        var text = _context.App.Get<string>(ModesAutoKeys.Fired);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveFired(Dictionary<string, string> fired)
    {
        // Rules that are gone leave no trace behind.
        var ids = _file.Rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in fired.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            fired.Remove(gone);
        }

        _context.App.Set(ModesAutoKeys.Fired, JsonSerializer.Serialize(fired));
    }

    // ------------------------------------------------------------------------------------------ firing and leaving

    /// <summary>
    /// Runs a rule whose trigger came. Returns false when it had to wait (a time rule tries again later), true when it was decided:
    /// started, asked, or skipped because a mode is active or the rule's mode is gone.
    /// </summary>
    private bool TryFire(ModeRule rule, bool isTime, string subject)
    {
        var away = _context.Services.Get<AwayController>()?.IsAway == true;
        if (!Active || away || _locked || _runtime.Elevated || _runtime.IsSwitching)
        {
            return false;
        }

        var mode = _runtime.Modes.FirstOrDefault(m => m.Id == rule.ModeId);
        var modes = (IModesService)_runtime;
        switch (ModesAutoMath.Decide(rule, modes.ActiveId, mode is not null))
        {
            case AutoDecision.Enter:
                _pending = (rule.Id, rule.ModeId, DateTime.Now + PendingLife);
                if (!_runtime.Enter(rule.ModeId, "auto"))
                {
                    _pending = null;
                }

                _context.Log.Info($"[modes] auto rule {rule.Id} enters {rule.ModeId}");
                break;
            case AutoDecision.Ask:
                _runtime.Suggest(
                    rule.Id,
                    rule.ModeId,
                    isTime ? ModesAutoStrings.IslandItsTime(subject, mode!.Name) : ModesAutoStrings.IslandAppOpened(subject, mode!.Name),
                    isTime ? ModesAutoStrings.ToastItsTime(subject, mode.Name) : ModesAutoStrings.ToastAppOpened(subject, mode.Name));
                _context.Log.Info($"[modes] auto rule {rule.Id} asks about {rule.ModeId}");
                break;
            default:
                _context.Log.Info($"[modes] auto rule {rule.Id} skipped ({(mode is null ? "the mode is gone" : "a mode is active")})");
                break;
        }

        return true;
    }

    /// <summary>Leaves the mode at the rule's "Leave at" time, but only when this rule started it and the person is still in it.</summary>
    private void TryLeave(ModeRule rule)
    {
        var modes = (IModesService)_runtime;
        if (_locked || _runtime.IsSwitching || modes.ActiveId != rule.ModeId || _context.App.Get<string>(ModesAutoKeys.StartedBy) != rule.Id)
        {
            return;
        }

        // Cleared first so the next tick of the grace period does not try again.
        _context.App.Set(ModesAutoKeys.StartedBy, string.Empty);
        _context.Log.Info($"[modes] auto rule {rule.Id} leaves {rule.ModeId}");
        _runtime.Leave("auto");
    }

    private void OnSuggestionAccepted(string ruleId, string modeId) => _pending = (ruleId, modeId, DateTime.Now + PendingLife);

    /// <summary>
    /// Keeps <see cref="ModesAutoKeys.StartedBy"/> true: set when the mode a rule asked for becomes active, cleared as soon as the active
    /// mode is something else or none, so "Leave at" can never end a mode the person chose themselves.
    /// </summary>
    private void OnRuntimeChanged()
    {
        if (_runtime.IsSwitching)
        {
            return;
        }

        var activeId = ((IModesService)_runtime).ActiveId;
        var startedBy = _context.App.Get<string>(ModesAutoKeys.StartedBy);
        if (_pending is { } pending)
        {
            if (DateTime.Now >= pending.Expires)
            {
                _pending = null;
            }
            else if (activeId == pending.ModeId)
            {
                _pending = null;
                if (startedBy != pending.RuleId)
                {
                    _context.App.Set(ModesAutoKeys.StartedBy, pending.RuleId);
                }

                return;
            }
        }

        if (startedBy.Length == 0)
        {
            return;
        }

        var rule = _file.Rules.FirstOrDefault(r => r.Id == startedBy);
        if (rule is null || activeId != rule.ModeId)
        {
            _context.App.Set(ModesAutoKeys.StartedBy, string.Empty);
        }
    }
}

/// <summary>What the runner asks Windows about a program: its image path and when it started, from one handle with the least access.</summary>
internal static class ProcessFacts
{
    /// <summary>The full image path and the creation time (FILETIME ticks); null when the process cannot be queried.</summary>
    public static (string Path, long Created)? Read(int pid)
    {
        var process = WinApi.OpenProcess(WinApi.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var name = new StringBuilder(1024);
            var size = (uint)name.Capacity;
            if (!WinApi.QueryFullProcessImageNameW(process, 0, name, ref size) || !WinApi.GetProcessTimes(process, out var created, out _, out _, out _))
            {
                return null;
            }

            return (name.ToString(), created);
        }
        finally
        {
            WinApi.CloseHandle(process);
        }
    }

    /// <summary>The name the person knows the program by: its file description, or the file name without ".exe".</summary>
    public static string DisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // the file name below is a fine fallback
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
