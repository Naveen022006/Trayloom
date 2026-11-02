// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>A hotkey mutes and unmutes your microphone system-wide, with an on-screen indicator while muted.</summary>
public sealed class MicMuteFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.M);

    private IFeatureContext? _context;
    private HotkeyBinding? _hotkey;
    private DeviceWatcher? _watcher;
    private MMDevice? _device;
    private Osd? _toast;
    private Osd? _indicator;
    private bool _muted;
    private bool _mutedByUs;

    public string Id => "mic-mute";

    public string Name => "Mute microphone";

    public string Description => "Mute or unmute your microphone from anywhere with one hotkey, with a clear on-screen indicator.";

    public FeatureGroup Group => FeatureGroup.Sound;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", _muted ? "Unmute microphone" : "Mute microphone", Toggle, _muted ? "" : ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _hotkey = new HotkeyBinding(context, "hotkey", DefaultHotkey, Toggle);
        context.Settings.Changed += OnSettingChanged;
        try
        {
            _watcher = new DeviceWatcher((change, _) =>
            {
                if (change == DeviceChange.DefaultChanged)
                {
                    _context?.Ui.BeginInvoke(Attach);
                }
            });
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not watch audio devices", ex);
        }

        Attach();
        UpdateIndicator();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        // Do not leave the microphone muted when the feature goes away.
        if (_mutedByUs && _muted)
        {
            ApplyMute(false);
        }

        _hotkey?.Dispose();
        _hotkey = null;
        _watcher?.Dispose();
        _watcher = null;
        Detach();
        _toast?.Close();
        _toast = null;
        _indicator?.Close();
        _indicator = null;
        _mutedByUs = false;
        _muted = false;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView() =>
        _context is null
            ? null
            : new SettingsViewBuilder(_context.Settings)
                .AddHotkey("hotkey", "Toggle microphone mute", DefaultHotkey)
                .AddToggle("showIndicator", "Show an indicator while muted", true, "A small pill in the top-right corner of the main screen.")
                .AddToggle("allDevices", "Mute every microphone", false, "Off: only the default microphone. On: all active recording devices.")
                .Build();

    private void Toggle()
    {
        if (_context is null)
        {
            return;
        }

        Attach();
        var target = !_muted;
        if (!ApplyMute(target))
        {
            _context.Notifier.Show("Microphone", "No microphone found to mute.", NotificationLevel.Warning);
            return;
        }

        _mutedByUs = target;
        _toast ??= new Osd();
        _toast.Display(target ? "" : "", target ? "Microphone muted" : "Microphone on", 1400);
    }

    private bool ApplyMute(bool mute)
    {
        try
        {
            var all = _context?.Settings.Get("allDevices", false) ?? false;
            var any = false;
            if (all)
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    using (device)
                    {
                        device.AudioEndpointVolume.Mute = mute;
                        any = true;
                    }
                }
            }
            else if (_device is not null)
            {
                _device.AudioEndpointVolume.Mute = mute;
                any = true;
            }

            if (any)
            {
                _muted = mute;
                UpdateIndicator();
            }

            return any;
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Could not change microphone mute", ex);
            return false;
        }
    }

    private void Attach()
    {
        Detach();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
            {
                _muted = false;
                UpdateIndicator();
                return;
            }

            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            _muted = _device.AudioEndpointVolume.Mute;
            UpdateIndicator();
        }
        catch (Exception ex)
        {
            _context?.Log.Warn($"Could not attach to the default microphone: {ex.Message}");
        }
    }

    private void Detach()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }
        catch (Exception)
        {
        }

        _device = null;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        // Raised on a COM thread when the mute state changes from anywhere (Settings, other apps, headset button).
        _context?.Ui.BeginInvoke(() =>
        {
            if (_context is null || _muted == data.Muted)
            {
                return;
            }

            _muted = data.Muted;
            if (!_muted)
            {
                _mutedByUs = false;
            }

            UpdateIndicator();
        });
    }

    private void OnSettingChanged(string key)
    {
        if (key == "showIndicator")
        {
            _context?.Ui.BeginInvoke(UpdateIndicator);
        }
    }

    private void UpdateIndicator()
    {
        if (_context is null)
        {
            return;
        }

        if (_muted && _context.Settings.Get("showIndicator", true))
        {
            _indicator ??= new Osd(corner: true);
            _indicator.Display("", "Microphone muted", 0);
        }
        else
        {
            _indicator?.Dismiss();
        }
    }
}
