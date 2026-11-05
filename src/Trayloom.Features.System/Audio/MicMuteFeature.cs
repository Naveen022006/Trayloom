// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>
/// Mute microphone (spec 05 D4): mutes the switch of EVERY capture endpoint, falling back to zero input volume (the previous level is
/// kept and given back on unmute). The state persists across launches, device changes re-apply an active mute, and a device this app
/// changed gets its level back when it returns.
/// </summary>
public sealed class MicMuteFeature : IFeature, IMicMuteState
{
    private IFeatureContext? _ctx;
    private IDisposable? _shortcut;
    private DeviceWatcher? _watcher;
    private Osd? _osd;
    private bool _muted;
    private bool _busy;

    public static MicMuteFeature? Instance { get; private set; }

    public string Id => "mic-mute";

    public bool IsMuted => _muted;

    public event Action? Changed;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", _muted ? Strings.MicMute.Unmute : Strings.MicMute.Title, Toggle, _muted ? "" : ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        Instance = this;
        SystemDefaults.Register(context.App);
        context.Services.Provide<IMicMuteState>(this);
        _shortcut = context.Shortcuts.Bind("micMute", Toggle);
        try
        {
            _watcher = new DeviceWatcher((change, _) =>
            {
                if (change != DeviceChange.StateChanged || true)
                {
                    _ctx?.Ui.BeginInvoke(Reapply);
                }
            });
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not watch audio devices", ex);
        }

        // The mute persists across relaunch.
        _muted = context.App.Get<bool>("micMuteActive");

        // A mute applied by an earlier version has no journal yet: write it now so an uninstall can still give the microphone back.
        MicMuteJournal.Write(_muted, context.App.Get<Dictionary<string, double>>("micMuteSavedLevels"));
        if (_muted)
        {
            Reapply();
        }

        RestoreReturned();
    }

    public void Stop()
    {
        // Uninstalling the feature must never leave the microphone muted (the mute only survives an app relaunch).
        if (_ctx is { } ctx && !ctx.Runtime.IsAvailable("mic-mute") && (_muted || ctx.App.Get<bool>("micMuteActive")))
        {
            try
            {
                Apply(false);
            }
            catch (Exception ex)
            {
                ctx.Log.Warn("Unmuting after uninstall failed: " + ex.Message);
            }

            _muted = false;
            ctx.App.Set("micMuteActive", false);
            MicMuteJournal.Write(false, ctx.App.Get<Dictionary<string, double>>("micMuteSavedLevels"));
        }

        _shortcut?.Dispose();
        _shortcut = null;
        _watcher?.Dispose();
        _watcher = null;
        _ctx?.Services.Withdraw<IMicMuteState>(this);
        _osd?.Close();
        _osd = null;
        if (Instance == this)
        {
            Instance = null;
        }

        _ctx = null;
    }

    /// <summary>Toggles the mute of every microphone and shows the HUD.</summary>
    public void Toggle()
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        var target = !_muted;
        var (ok, total) = Apply(target);
        if (total == 0)
        {
            Hud(Gl.MicOff, Strings.MicMute.Partial);
            return;
        }

        _muted = ok > 0 && target;
        if (!target)
        {
            _muted = false;
        }

        ctx.App.Set("micMuteActive", _muted);
        MicMuteJournal.Write(_muted, ctx.App.Get<Dictionary<string, double>>("micMuteSavedLevels"));
        Changed?.Invoke();
        if (ok < total)
        {
            Hud(Gl.MicOff, target ? Strings.MicMute.Partial : Strings.MicMute.StillMuted);
        }
        else
        {
            Hud(target ? Gl.MicOff : Gl.Mic, target ? Strings.MicMute.On : Strings.MicMute.Off);
        }
    }

    private void Reapply()
    {
        if (_ctx is null || _busy)
        {
            return;
        }

        if (_muted)
        {
            _busy = true;
            try
            {
                Apply(true);
            }
            finally
            {
                _busy = false;
            }
        }

        RestoreReturned();
    }

    /// <summary>Applies the mute to every active capture endpoint; returns (succeeded, total).</summary>
    private (int Ok, int Total) Apply(bool mute)
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return (0, 0);
        }

        var levels = ctx.App.Get<Dictionary<string, double>>("micMuteSavedLevels");
        var ok = 0;
        var total = 0;
        var levelsChanged = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    total++;
                    try
                    {
                        var volume = device.AudioEndpointVolume;
                        if (mute)
                        {
                            var level = volume.MasterVolumeLevelScalar;
                            if (level > 0.01f && !levels.ContainsKey(device.ID))
                            {
                                levels[device.ID] = level;
                                levelsChanged = true;

                                // On disk before the level is touched, so an uninstall can give it back even after a crash.
                                MicMuteJournal.Write(true, levels);
                            }

                            volume.Mute = true;
                            if (!volume.Mute)
                            {
                                volume.MasterVolumeLevelScalar = 0f;
                            }

                            ok++;
                        }
                        else
                        {
                            volume.Mute = false;
                            if (levels.TryGetValue(device.ID, out var saved))
                            {
                                if (volume.MasterVolumeLevelScalar < 0.01f)
                                {
                                    volume.MasterVolumeLevelScalar = (float)Math.Clamp(saved, 0.01, 1);
                                }

                                levels.Remove(device.ID);
                                levelsChanged = true;
                            }

                            ok++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.Log.Warn($"Microphone '{device.ID}' could not be changed: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ctx.Log.Error("Could not change microphone mute", ex);
        }

        if (levelsChanged)
        {
            ctx.App.Set("micMuteSavedLevels", levels);
            MicMuteJournal.Write(mute || _muted, levels);
        }

        return (ok, total);
    }

    /// <summary>A microphone this app silenced while it was unplugged gets its level back when it returns (unless muting is active).</summary>
    private void RestoreReturned()
    {
        var ctx = _ctx;
        if (ctx is null || _muted)
        {
            return;
        }

        var levels = ctx.App.Get<Dictionary<string, double>>("micMuteSavedLevels");
        if (levels.Count > 0)
        {
            Apply(false);
        }
    }

    private void Hud(string glyph, string text)
    {
        _osd ??= new Osd();
        _osd.Display(glyph, text, 1400);
    }
}

/// <summary>Quick tools page: the Mute microphone section (anchor <c>micMute</c>).</summary>
public sealed class MicMuteSettingsSection : ISettingsSection
{
    public string? FeatureId => "mic-mute";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "micMute";

    public string? Title => Loc.T("Mute microphone");

    public int Order => 100;

    public IReadOnlyList<string> Keywords => ["mic mute", "indicator", "notification area", "global shortcut", "microphone"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new SettingsCard(Strings.MicMute.Title);
        var feature = MicMuteFeature.Instance;
        var muted = feature?.IsMuted == true;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal };
        var buttonHost = new ContentControl();
        Button Make(bool isMuted) => UiKit.Button(isMuted ? Strings.MicMute.Unmute : Strings.MicMute.Title, ButtonKind.Bordered, 12, isMuted ? Gl.MicOff : Gl.Mic, () => feature?.Toggle());
        buttonHost.Content = Make(muted);
        buttonRow.Children.Add(buttonHost);
        var state = UiKit.Icon(Gl.MicOff, 12, ThemeKeys.Orange);
        var stateText = UiKit.Text(Strings.MicMute.On, 12, FontWeights.Medium, ThemeKeys.Orange);
        stateText.Margin = new Thickness(6, 0, 0, 0);
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = muted ? Visibility.Visible : Visibility.Collapsed };
        stateRow.Children.Add(state);
        stateRow.Children.Add(stateText);
        buttonRow.Children.Add(stateRow);
        card.Add(buttonRow);
        card.AddCaption(Strings.MicMute.Caption);
        card.AddDivider();
        card.Add(new SettingsRow(Gl.MicOff, Strings.MicMute.Indicator, Strings.MicMute.IndicatorCaption, new MacSwitch(app.Get<bool>("micMuteMenuBarIndicator"), SwitchSize.Regular, on => app.Set("micMuteMenuBarIndicator", on))));
        card.AddDivider();
        var shortcutHost = new ContentControl { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
        void Shortcut() => shortcutHost.Content = app.Get<bool>("micMuteShortcutEnabled") ? ShortcutRows.Create(c, "micMute", Strings.OutputSwitcher.Shortcut, 12, Strings.OutputSwitcher.Rejected) : null;
        card.Add(new SettingsRow(Gl.Keyboard, Strings.MicMute.GlobalShortcut, null, new MacSwitch(app.Get<bool>("micMuteShortcutEnabled"), SwitchSize.Regular, on =>
        {
            app.Set("micMuteShortcutEnabled", on);
            Shortcut();
        })));
        Shortcut();
        card.Add(shortcutHost);

        if (feature is not null)
        {
            void Refresh()
            {
                var now = feature.IsMuted;
                buttonHost.Content = Make(now);
                stateRow.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
            }

            void Changed() => card.Dispatcher.BeginInvoke(Refresh);
            card.Loaded += (_, _) => feature.Changed += Changed;
            card.Unloaded += (_, _) => feature.Changed -= Changed;
        }

        return card;
    }
}

/// <summary>The record of a mute Trayloom applied (<c>mic-mute.json</c>), so uninstall can give the microphone back.</summary>
internal static class MicMuteJournal
{
    public const string FileName = "mic-mute.json";

    /// <summary>The file in the feature's data folder.</summary>
    public static string PathIn(string dataDir) => Path.Combine(dataDir, FileName);

    /// <summary>Writes the record into the real data folder; best effort. Nothing to remember deletes it.</summary>
    public static void Write(bool active, IReadOnlyDictionary<string, double> levels)
    {
        try
        {
            Write(AppPaths.FeatureData("mic-mute"), active, levels);
        }
        catch
        {
            // the setting still holds the same state; the journal is the safety net
        }
    }

    /// <summary>Writes the record into <paramref name="dataDir"/>; nothing to remember deletes it.</summary>
    public static void Write(string dataDir, bool active, IReadOnlyDictionary<string, double> levels)
    {
        var path = PathIn(dataDir);
        if (!active && levels.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new Entry(active, new Dictionary<string, double>(levels))));
    }

    /// <summary>The record, or null when there is none or it is unreadable.</summary>
    public static (bool Active, Dictionary<string, double> Levels)? Read(string dataDir)
    {
        try
        {
            var path = PathIn(dataDir);
            if (!File.Exists(path))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            return entry is null ? null : (entry.Active, entry.Levels ?? []);
        }
        catch
        {
            return null;
        }
    }

    private sealed record Entry(bool Active, Dictionary<string, double>? Levels);
}

/// <summary>One capture endpoint as the mic recovery sees it (a seam so the decision can be tested).</summary>
internal interface IMicEndpoint
{
    string Id { get; }

    bool Mute { get; set; }

    float Level { get; set; }
}

/// <summary>
/// On uninstall, un-mutes every microphone and gives back a level Trayloom set to zero. Acts only when the mic journal says a mute was
/// applied. Startup does nothing: the mute persists across restarts by design.
/// </summary>
public sealed class MicMuteRecovery : IStartupRecovery
{
    private readonly Func<string> _dataDir;
    private readonly Func<IReadOnlyList<IMicEndpoint>> _endpoints;

    public MicMuteRecovery()
        : this(() => AppPaths.FeatureData("mic-mute"), NAudioEndpoints.Capture)
    {
    }

    internal MicMuteRecovery(Func<string> dataDir, Func<IReadOnlyList<IMicEndpoint>> endpoints)
    {
        _dataDir = dataDir;
        _endpoints = endpoints;
    }

    public string Id => "mic-mute";

    public void Recover(RecoveryStage stage, ILog log)
    {
        if (stage != RecoveryStage.Uninstall)
        {
            return;
        }

        var dir = _dataDir();
        var journal = MicMuteJournal.Read(dir);
        if (journal is not { } record || (!record.Active && record.Levels.Count == 0))
        {
            return;
        }

        var failed = false;
        foreach (var endpoint in _endpoints())
        {
            // No active mute: only a microphone Trayloom silenced (it has a saved level) is touched, never one muted in Windows.
            if (!record.Active && !record.Levels.ContainsKey(endpoint.Id))
            {
                continue;
            }

            try
            {
                endpoint.Mute = false;
                if (record.Levels.TryGetValue(endpoint.Id, out var saved) && endpoint.Level < 0.01f)
                {
                    endpoint.Level = (float)Math.Clamp(saved, 0.01, 1);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                log.Warn($"mic-mute: a microphone could not be given back: {ex.Message}");
            }
        }

        if (!failed)
        {
            try
            {
                File.Delete(MicMuteJournal.PathIn(dir));
            }
            catch
            {
                // ignore
            }
        }
    }
}

/// <summary>The real capture endpoints.</summary>
internal sealed class NAudioEndpoints : IMicEndpoint
{
    private readonly MMDevice _device;

    private NAudioEndpoints(MMDevice device)
    {
        _device = device;
    }

    public string Id => _device.ID;

    public bool Mute
    {
        get => _device.AudioEndpointVolume.Mute;
        set => _device.AudioEndpointVolume.Mute = value;
    }

    public float Level
    {
        get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        set => _device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
    }

    public static IReadOnlyList<IMicEndpoint> Capture()
    {
        var list = new List<IMicEndpoint>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add(new NAudioEndpoints(device));
            }
        }
        catch
        {
            // none
        }

        return list;
    }
}
