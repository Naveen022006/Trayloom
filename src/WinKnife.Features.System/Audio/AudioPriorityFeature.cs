// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Keeps the default speakers and microphone on your highest-priority connected device.</summary>
public sealed class AudioPriorityFeature : IFeature
{
    private IFeatureContext? _context;
    private DeviceWatcher? _watcher;
    private DispatcherTimer? _debounce;

    public string Id => "audio-priority";

    public string Name => "Audio device priority";

    public string Description => "Rank your sound devices and WinKnife switches to the best connected one whenever devices come or go.";

    public FeatureGroup Group => FeatureGroup.Sound;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("apply", "Apply audio priority now", () => Apply(notify: true), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(1200), DispatcherPriority.Background, (_, _) =>
        {
            _debounce?.Stop();
            Apply(notify: true);
        }, context.Ui);
        _debounce.Stop();
        context.Settings.Changed += OnSettingChanged;
        try
        {
            _watcher = new DeviceWatcher(OnDeviceChange);
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not watch audio devices", ex);
        }

        Apply(notify: false);
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Stop();
        _debounce = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(DeviceEditors.PriorityList(_context.Settings, "output", DataFlow.Render, "Playback priority", "The first connected device in this list becomes the default output."));
        var input = DeviceEditors.PriorityList(_context.Settings, "input", DataFlow.Capture, "Microphone priority", "The first connected device in this list becomes the default input.");
        input.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(input);
        return panel;
    }

    private void OnSettingChanged(string key)
    {
        if (key is "output" or "input")
        {
            Restart();
        }
    }

    private void OnDeviceChange(DeviceChange change, string id)
    {
        // Switching the default ourselves raises DefaultChanged; only react to devices arriving or leaving.
        if (change != DeviceChange.DefaultChanged)
        {
            Restart();
        }
    }

    private void Restart()
    {
        _context?.Ui.BeginInvoke(() =>
        {
            _debounce?.Stop();
            _debounce?.Start();
        });
    }

    private void Apply(bool notify)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                ApplyFlow(context, DataFlow.Render, "output", "Sound output", notify);
                ApplyFlow(context, DataFlow.Capture, "input", "Microphone", notify);
            }
            catch (Exception ex)
            {
                context.Log.Error("Audio priority failed", ex);
            }
        });
    }

    private static void ApplyFlow(IFeatureContext context, DataFlow flow, string key, string title, bool notify)
    {
        var priorities = context.Settings.Get(key, new List<DevicePref>());
        if (priorities.Count == 0)
        {
            return;
        }

        var active = AudioDevices.List(flow);
        var best = priorities.Select(p => active.FirstOrDefault(a => a.Id == p.Id)).FirstOrDefault(a => a is not null);
        if (best is null || AudioDevices.DefaultId(flow) == best.Id)
        {
            return;
        }

        if (AudioDevices.SetDefault(best.Id))
        {
            context.Log.Info($"Audio priority: default {key} -> {best.Name}");
            if (notify)
            {
                context.Notifier.Show(title, $"Switched to {best.Name}");
            }
        }
    }
}
