// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>Output switcher: a shortcut moves the default playback device to the next available device of your cycle.</summary>
public sealed class OutputSwitcherFeature : IFeature
{
    private IFeatureContext? _ctx;
    private IDisposable? _shortcut;
    private Osd? _osd;
    private int _busy;

    public string Id => "sound-output-switcher";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("cycle", Loc.T("Switch sound output"), Cycle, ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        _shortcut = context.Shortcuts.Bind("soundOutputSwitcher", Cycle);
    }

    public void Stop()
    {
        _shortcut?.Dispose();
        _shortcut = null;
        _osd?.Close();
        _osd = null;
        _ctx = null;
    }

    private void Cycle()
    {
        var ctx = _ctx;
        if (ctx is null || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var selected = ctx.App.Get<List<string>>("soundOutputSwitcherDeviceUIDs");
                var active = AudioDevices.List(DataFlow.Render);
                var candidates = active.Where(a => selected.Contains(a.Id)).ToList();
                if (candidates.Count == 0)
                {
                    Show(Gl.Warning, Strings.OutputSwitcher.SelectOne);
                    return;
                }

                var current = AudioDevices.DefaultId(DataFlow.Render);
                var index = candidates.FindIndex(c => c.Id == current);
                var next = candidates[(index + 1) % candidates.Count];
                if (next.Id == current)
                {
                    Show("", next.Name);
                    return;
                }

                if (AudioDevices.SetDefault(next.Id))
                {
                    ctx.Log.Info($"Default output -> {next.Name}");
                    Show("", next.Name);
                }
                else
                {
                    Show(Gl.Warning, Strings.Mixer.SwitchFailed(next.Name));
                }
            }
            catch (Exception ex)
            {
                ctx.Log.Error("Output switch failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private void Show(string glyph, string text)
    {
        _ctx?.Ui.BeginInvoke(() =>
        {
            if (_ctx is null)
            {
                return;
            }

            _osd ??= new Osd();
            _osd.Display(glyph, text);
        });
    }
}

/// <summary>General page: the Output switcher card (anchor <c>soundOutputSwitcher</c>).</summary>
public sealed class OutputSwitcherSettingsSection : ISettingsSection
{
    public string? FeatureId => "sound-output-switcher";

    public string PageId => SettingsPages.General;

    public string? Anchor => "soundOutputSwitcher";

    public string? Title => Loc.T("Output switcher");

    public int Order => 200;

    public IReadOnlyList<string> Keywords => ["switch outputs with shortcut", "outputs in cycle", "shortcut"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        var card = new SettingsCard(Strings.OutputSwitcher.Title);
        card.Add(OutputSwitcherView.Build(context, compact: false));
        return card;
    }
}

/// <summary>
/// Audio device priority (spec 05 D3): keeps the default output and microphone on the highest-priority connected device of the lists
/// the user ordered. Like the Mac version it only steps in when a device connects or disconnects, or when the lists change; a device
/// picked by hand in Windows stays picked. Devices not ranked yet wait a moment for Windows to settle before they join the list: first
/// when Windows switched to them, or (Windows does not always switch) when they are headphones; below the other hardware otherwise.
/// A Bluetooth hands-free endpoint never goes first by itself, since using it drops the headset to call quality.
/// </summary>
public sealed class AudioPriorityFeature : IFeature
{
    private static readonly string[] VirtualMarkers =
    [
        "virtual", "vb-audio", "voicemeeter", "steam streaming", "oculus", "nvidia broadcast", "loopback", "cable ", "obs", "ndi", "wave link", "elgato", "droidcam",
    ];

    private IFeatureContext? _ctx;
    private DeviceWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _settle;
    private readonly HashSet<string> _waiting = [];
    private readonly Dictionary<DataFlow, HashSet<string>> _settled = [];
    private readonly HashSet<DataFlow> _pending = [];
    private bool _writing;

    public string Id => "audio-priority";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("apply", Loc.T("Apply audio priority now"), () => Sync(settle: false, force: true), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
        {
            _debounce?.Stop();
            Sync(settle: true, force: false);
        }, context.Ui);
        _debounce.Stop();
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(2000), DispatcherPriority.Background, (_, _) =>
        {
            _settle?.Stop();
            Sync(settle: false, force: false);
        }, context.Ui);
        _settle.Stop();
        context.App.Changed += OnSetting;
        try
        {
            _watcher = new DeviceWatcher(OnDeviceChange);
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not watch audio devices", ex);
        }

        // The first pass takes the baseline and gives unranked devices their place; it does not override what is in use.
        Sync(settle: true, force: false);
    }

    public void Stop()
    {
        if (_ctx is not null)
        {
            _ctx.App.Changed -= OnSetting;
        }

        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Stop();
        _debounce = null;
        _settle?.Stop();
        _settle = null;
        _waiting.Clear();
        _settled.Clear();
        _pending.Clear();
        _ctx = null;
    }

    private void OnSetting(string key)
    {
        if (key is "audioPriorityOutputEnabled" or "audioPriorityInputEnabled" or "audioPriorityOutputUIDs" or "audioPriorityInputUIDs")
        {
            if (!_writing)
            {
                _ctx?.Ui.BeginInvoke(() => Sync(settle: false, force: true));
            }
        }
    }

    private void OnDeviceChange(DeviceChange change, string id) =>
        _ctx?.Ui.BeginInvoke(() =>
        {
            _debounce?.Stop();
            _debounce?.Start();
        });

    private void Sync(bool settle, bool force)
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        try
        {
            SyncFlow(ctx, DataFlow.Render, "audioPriorityOutputUIDs", "audioPriorityOutputEnabled", settle, force);
            SyncFlow(ctx, DataFlow.Capture, "audioPriorityInputUIDs", "audioPriorityInputEnabled", settle, force);
        }
        catch (Exception ex)
        {
            ctx.Log.Error("Audio priority failed", ex);
        }
    }

    private void SyncFlow(IFeatureContext ctx, DataFlow flow, string listKey, string enabledKey, bool settle, bool force)
    {
        var active = AudioDevices.List(flow);
        var list = ctx.App.Get<List<string>>(listKey).ToList();
        var names = ctx.App.Get<Dictionary<string, string>>("audioPriorityDeviceNames");
        var current = AudioDevices.DefaultId(flow);
        var namesChanged = false;
        foreach (var d in active)
        {
            if (!names.TryGetValue(d.Id, out var old) || old != d.Name)
            {
                names[d.Id] = d.Name;
                namesChanged = true;
            }
        }

        // Only a device connecting or disconnecting is a priority event. Windows changing the default, or the person picking a
        // device by hand, is not, so a manual choice stays until the hardware changes.
        var ids = active.Select(a => a.Id).ToHashSet();
        if (_settled.TryGetValue(flow, out var before) && !before.SetEquals(ids))
        {
            _pending.Add(flow);
        }

        _settled[flow] = ids;
        if (force)
        {
            _pending.Add(flow);
        }

        _waiting.RemoveWhere(id => id.Contains(flow == DataFlow.Render ? "{0.0.0." : "{0.0.1.", StringComparison.Ordinal) && !ids.Contains(id));

        var changed = false;
        if (list.Count == 0 && active.Count > 0)
        {
            // Seed: the current device, then headphones, then the other hardware, then virtual and aggregate devices.
            var hardware = active.Where(a => !IsVirtual(a.Name)).ToList();
            var virtuals = active.Where(a => IsVirtual(a.Name)).ToList();
            var seed = new List<string>();
            if (current is not null && active.Any(a => a.Id == current))
            {
                seed.Add(current);
            }

            seed.AddRange(hardware.Where(a => IsHeadphones(flow, a)).Select(a => a.Id).Where(i => !seed.Contains(i)));
            seed.AddRange(hardware.Select(a => a.Id).Where(i => !seed.Contains(i)));
            seed.AddRange(virtuals.Select(a => a.Id).Where(i => !seed.Contains(i)));
            list = seed;
            changed = true;
        }
        else
        {
            foreach (var device in active.Where(a => !list.Contains(a.Id)))
            {
                // A device that is not ranked yet waits until Windows settles on it before it gets its place.
                if (settle)
                {
                    if (_waiting.Add(device.Id))
                    {
                        _settle?.Stop();
                        _settle?.Start();
                    }

                    continue;
                }

                if (!_waiting.Remove(device.Id))
                {
                    continue;
                }

                if ((device.Id == current && !IsHandsFree(device.Name)) || IsHeadphones(flow, device))
                {
                    // Windows (or the person) switched to it, or it is headphones that just connected: it goes first and takes over.
                    list.Insert(0, device.Id);
                    _pending.Add(flow);
                }
                else if (IsVirtual(device.Name))
                {
                    list.Add(device.Id);
                }
                else
                {
                    var firstVirtual = list.FindIndex(i => names.TryGetValue(i, out var n) && IsVirtual(n));
                    list.Insert(firstVirtual < 0 ? list.Count : firstVirtual, device.Id);
                }

                changed = true;
            }
        }

        if (changed || namesChanged)
        {
            _writing = true;
            try
            {
                if (changed)
                {
                    ctx.App.Set(listKey, list);
                }

                if (namesChanged)
                {
                    ctx.App.Set("audioPriorityDeviceNames", names);
                }
            }
            finally
            {
                _writing = false;
            }
        }

        // Wait while a device of this direction still has no place, so a headset Windows just moved to is not taken back first.
        if (!_pending.Contains(flow) || active.Any(a => _waiting.Contains(a.Id)))
        {
            return;
        }

        _pending.Remove(flow);
        if (!ctx.App.Get<bool>(enabledKey) || list.Count == 0)
        {
            return;
        }

        var best = list.Select(id => active.FirstOrDefault(a => a.Id == id)).FirstOrDefault(a => a is not null);
        if (best is null || best.Id == current)
        {
            return;
        }

        if (AudioDevices.SetDefault(best.Id))
        {
            ctx.Log.Info($"Audio priority: default {(flow == DataFlow.Render ? "output" : "input")} -> {best.Name}");
        }
    }

    private static bool IsVirtual(string name) => VirtualMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>A Bluetooth headset's call endpoint ("Headset (... Hands-Free)"): low quality, and using it puts the headset in call mode.</summary>
    private static bool IsHandsFree(string name) =>
        name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase);

    /// <summary>Headphones or a headset for listening (wired, USB or Bluetooth), which people expect to take over when they connect.</summary>
    private static bool IsHeadphones(DataFlow flow, AudioEndpoint device) =>
        flow == DataFlow.Render && device.IsHeadphones && !IsHandsFree(device.Name) && !IsVirtual(device.Name);
}

/// <summary>General page: the Audio priority card (anchor <c>audioPriority</c>).</summary>
public sealed class AudioPrioritySettingsSection : ISettingsSection
{
    public string? FeatureId => "audio-priority";

    public string PageId => SettingsPages.General;

    public string? Anchor => "audioPriority";

    public string? Title => Loc.T("Audio priority");

    public int Order => 300;

    public IReadOnlyList<string> Keywords => ["output priority", "microphone priority", "automatically switch", "devices"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        var card = new SettingsCard(Strings.AudioPriority.Title);
        card.Add(AudioPriorityView.Build(context, compact: false));
        return card;
    }
}
