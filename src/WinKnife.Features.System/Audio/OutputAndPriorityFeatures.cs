// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

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
/// the user ordered. Devices not ranked yet wait a moment for the OS to settle before they join the list.
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
    private bool _writing;

    public string Id => "audio-priority";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("apply", Loc.T("Apply audio priority now"), () => Sync(settle: false), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
        {
            _debounce?.Stop();
            Sync(settle: true);
        }, context.Ui);
        _debounce.Stop();
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(1800), DispatcherPriority.Background, (_, _) =>
        {
            _settle?.Stop();
            Sync(settle: false);
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

        Sync(settle: false);
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
        _ctx = null;
    }

    private void OnSetting(string key)
    {
        if (key is "audioPriorityOutputEnabled" or "audioPriorityInputEnabled" or "audioPriorityOutputUIDs" or "audioPriorityInputUIDs")
        {
            if (!_writing)
            {
                _ctx?.Ui.BeginInvoke(() => Sync(settle: false));
            }
        }
    }

    private void OnDeviceChange(DeviceChange change, string id) =>
        _ctx?.Ui.BeginInvoke(() =>
        {
            _debounce?.Stop();
            _debounce?.Start();
        });

    private void Sync(bool settle)
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        try
        {
            SyncFlow(ctx, DataFlow.Render, "audioPriorityOutputUIDs", "audioPriorityOutputEnabled", settle);
            SyncFlow(ctx, DataFlow.Capture, "audioPriorityInputUIDs", "audioPriorityInputEnabled", settle);
        }
        catch (Exception ex)
        {
            ctx.Log.Error("Audio priority failed", ex);
        }
    }

    private void SyncFlow(IFeatureContext ctx, DataFlow flow, string listKey, string enabledKey, bool settle)
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

        var changed = false;
        if (list.Count == 0 && active.Count > 0)
        {
            // Seed: the current device, then built-in / real hardware, then virtual and aggregate devices.
            var hardware = active.Where(a => !IsVirtual(a.Name)).ToList();
            var virtuals = active.Where(a => IsVirtual(a.Name)).ToList();
            var seed = new List<string>();
            if (current is not null && active.Any(a => a.Id == current))
            {
                seed.Add(current);
            }

            seed.AddRange(hardware.Select(a => a.Id).Where(i => !seed.Contains(i)));
            seed.AddRange(virtuals.Select(a => a.Id).Where(i => !seed.Contains(i)));
            list = seed;
            changed = true;
        }
        else
        {
            foreach (var device in active.Where(a => !list.Contains(a.Id)))
            {
                // A device that is not ranked yet waits until the OS settles on it: it joins first when the OS switched to it,
                // below the other hardware if not.
                if (settle && _waiting.Add(device.Id))
                {
                    _settle?.Stop();
                    _settle?.Start();
                    continue;
                }

                if (!settle && !_waiting.Contains(device.Id))
                {
                    continue;
                }

                _waiting.Remove(device.Id);
                if (device.Id == current)
                {
                    list.Insert(0, device.Id);
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
