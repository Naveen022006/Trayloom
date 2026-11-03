// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>Finer volume steps: the volume keys are swallowed and move the default output by 1 % (instead of Windows' 2 %).</summary>
internal static class PreciseVolume
{
    /// <summary>True when the keyboard hook could not be installed.</summary>
    public static bool Failed { get; set; }
}

/// <summary>
/// Volume mixer: per-app volume, pinning, custom order, per-app output, plus the services that need no window: lowering the volume when
/// headphones disconnect and the finer volume keys. The mixer itself is drawn by <see cref="MixerView"/> in the tray panel and on Settings.
/// </summary>
public sealed class VolumeMixerFeature : IFeature
{
    private IFeatureContext? _ctx;
    private DeviceWatcher? _watcher;
    private AppMemoryWatcher? _memory;
    private DispatcherTimer? _memoryTimer;
    private IDisposable? _keys;
    private Osd? _osd;
    private DispatcherTimer? _guardTimer;
    private string? _guardDefault;
    private readonly Dictionary<string, bool> _headphoneIds = [];

    public string Id => "mixer";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Volume mixer"), () => _ctx?.Shell.OpenPanel(PanelTabs.Mixer), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        AppRouting.Log = context.Log;
        AppRouting.WarmUp();
        SystemDefaults.Register(context.App);
        context.App.Changed += OnSetting;
        try
        {
            _watcher = new DeviceWatcher(OnDevice, OnState);
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not watch audio devices", ex);
        }

        _guardTimer = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(700) };
        _guardTimer.Tick += (_, _) => ApplyGuard();
        RememberHeadphones();
        ApplyKeys();

        // Per-app volume, mute and output: give each app back what the user chose, now and whenever its audio session appears again.
        _memory = new AppMemoryWatcher(context.App, context.Log);
        _memoryTimer = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(600) };
        _memoryTimer.Tick += (_, _) =>
        {
            _memoryTimer?.Stop();
            _memory?.Attach();
        };
        _memoryTimer.Start();
    }

    private void OnDevice(DeviceChange change, string id)
    {
        // COM thread. Outputs that come and go change which sessions exist; re-attach once things settle.
        if (change is DeviceChange.Added or DeviceChange.Removed or DeviceChange.StateChanged or DeviceChange.DefaultChanged)
        {
            _ctx?.Ui.BeginInvoke(() =>
            {
                _memoryTimer?.Stop();
                _memoryTimer?.Start();
            });
        }
    }

    public void Stop()
    {
        if (_ctx is not null)
        {
            _ctx.App.Changed -= OnSetting;
        }

        _guardTimer?.Stop();
        _guardTimer = null;
        _memoryTimer?.Stop();
        _memoryTimer = null;
        _memory?.Dispose();
        _memory = null;
        _watcher?.Dispose();
        _watcher = null;
        _keys?.Dispose();
        _keys = null;
        _osd?.Close();
        _osd = null;
        _ctx = null;
    }

    private void OnSetting(string key)
    {
        if (key == "preciseVolumeRollerEnabled")
        {
            _ctx?.Ui.BeginInvoke(ApplyKeys);
        }
    }

    // ------------------------------------------------------------------------------------------------ finer volume steps

    private void ApplyKeys()
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        _keys?.Dispose();
        _keys = null;
        PreciseVolume.Failed = false;
        if (!ctx.App.Get<bool>("preciseVolumeRollerEnabled"))
        {
            return;
        }

        try
        {
            _keys = ctx.Hooks.SubscribeKeyboard(OnKey, 10);
        }
        catch (Exception ex)
        {
            PreciseVolume.Failed = true;
            ctx.Log.Warn($"Volume key hook failed: {ex.Message}");
        }
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        // Hook thread: only classify here.
        if (e.VkCode is not (0xAE or 0xAF) || e.IsFromTrayloom)
        {
            return false;
        }

        if (e.IsKeyDown)
        {
            var up = e.VkCode == 0xAF;
            _ctx?.Ui.BeginInvoke(() => Step(up ? 0.01f : -0.01f));
        }

        return true;
    }

    private void Step(float delta)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            var next = Math.Clamp(volume.MasterVolumeLevelScalar + delta, 0f, 1f);
            volume.MasterVolumeLevelScalar = next;
            if (next > 0.001f && volume.Mute)
            {
                volume.Mute = false;
            }

            _osd ??= new Osd();
            _osd.Display(next <= 0.001f ? Gl.SpeakerMute : Gl.Speaker, $"{Math.Round(next * 100):0}%", 900);
        }
        catch (Exception)
        {
        }
    }

    // ------------------------------------------------------------------------------------------------ headphone guard

    private void RememberHeadphones()
    {
        foreach (var d in AudioDevices.List(DataFlow.Render))
        {
            _headphoneIds[d.Id] = d.IsHeadphones || d.Name.Contains("headphone", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("headset", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnState(string deviceId, DeviceState state)
    {
        // COM thread. An active endpoint is remembered; leaving the active state is the "disconnect".
        if (state == DeviceState.Active)
        {
            _ctx?.Ui.BeginInvoke(RememberHeadphones);
            return;
        }

        _ctx?.Ui.BeginInvoke(() =>
        {
            if (_ctx is null || !_ctx.App.Get<bool>("mixerLowerVolumeOnHeadphonesDisconnect"))
            {
                return;
            }

            if (_headphoneIds.TryGetValue(deviceId, out var isHeadphones) && isHeadphones)
            {
                _headphoneIds.Remove(deviceId);
                _guardTimer?.Stop();
                _guardTimer?.Start();
            }
        });
    }

    private void ApplyGuard()
    {
        _guardTimer?.Stop();
        var ctx = _ctx;
        if (ctx is null)
        {
            return;
        }

        var percent = Math.Clamp(ctx.App.Get<int>("mixerHeadphonesDisconnectVolumePercent"), 0, 100);
        if (percent == 0 && !ctx.App.Get<bool>("mixerHeadphonesDisconnectSilenceAllowed"))
        {
            percent = 1;
        }

        var target = AudioDevices.DefaultId(DataFlow.Render);
        _guardDefault = target;
        if (target is null)
        {
            return;
        }

        var reading = AudioDevices.ReadVolume(target);
        if (reading is { } r && r.Volume * 100f > percent)
        {
            AudioDevices.WriteVolume(target, percent / 100f);
            ctx.Log.Info($"Headphones disconnected: output volume lowered to {percent}%");
        }
    }
}

/// <summary>VOLUME MIXER tab of the tray panel.</summary>
public sealed class MixerPanelSection : IPanelSection
{
    public string? FeatureId => "mixer";

    public string TabId => PanelTabs.Mixer;

    public int Order => 0;

    public FrameworkElement CreateView(IPanelContext context) => MixerView.Create(context, settingsMode: false);
}

/// <summary>Audio priority in the mixer tab when the volume mixer itself is not installed.</summary>
public sealed class AudioPriorityPanelSection : IPanelSection
{
    public string? FeatureId => "audio-priority";

    public string TabId => PanelTabs.Mixer;

    public int Order => 10;

    public FrameworkElement CreateView(IPanelContext context) =>
        context.Runtime.IsAvailable("mixer")
            ? new System.Windows.Controls.Border { Visibility = Visibility.Collapsed }
            : MixerView.CreatePriorityOnly(context);
}

/// <summary>General page: the Volume mixer card (anchor <c>mixer</c>).</summary>
public sealed class MixerSettingsSection : ISettingsSection
{
    public string? FeatureId => "mixer";

    public string PageId => SettingsPages.General;

    public string? Anchor => "mixer";

    public string? Title => Loc.T("Volume mixer");

    public int Order => 100;

    public IReadOnlyList<string> Keywords =>
    [
        "output", "system sounds", "microphone", "hide inactive apps", "lower volume when headphones disconnect", "volume after disconnect",
        "finer volume steps", "apps in the list", "pin", "per-app volume",
    ];

    public FrameworkElement CreateView(ISettingsContext context) => MixerView.Create(context, settingsMode: true);
}
