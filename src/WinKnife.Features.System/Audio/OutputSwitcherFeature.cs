// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>A hotkey cycles the default playback device through the devices you picked.</summary>
public sealed class OutputSwitcherFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.O);

    private IFeatureContext? _context;
    private HotkeyBinding? _hotkey;
    private Osd? _osd;
    private int _busy;

    public string Id => "output-switcher";

    public string Name => "Sound output switcher";

    public string Description => "Press a hotkey to cycle your default speakers, headphones and other playback devices.";

    public FeatureGroup Group => FeatureGroup.Sound;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("cycle", "Switch sound output", Cycle, ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _hotkey = new HotkeyBinding(context, "hotkey", DefaultHotkey, Cycle);
    }

    public void Stop()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _osd?.Close();
        _osd = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new SettingsViewBuilder(_context.Settings)
            .AddHotkey("hotkey", "Switch output", DefaultHotkey, "Cycles through the devices ticked below, in the order they are listed in Windows.")
            .Build());
        var list = DeviceEditors.Checklist(_context.Settings, "devices", DataFlow.Render, "Devices to cycle through", "Leave everything unticked to cycle through all active playback devices.");
        list.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(list);
        return panel;
    }

    private void Cycle()
    {
        var context = _context;
        if (context is null || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var selected = context.Settings.Get("devices", new List<DevicePref>());
                var active = AudioDevices.List(DataFlow.Render);
                var candidates = selected.Count > 0 ? active.Where(a => selected.Any(s => s.Id == a.Id)).ToList() : active;
                if (candidates.Count == 0)
                {
                    Show(context, "", "No playback devices to switch between");
                    return;
                }

                var current = AudioDevices.DefaultId(DataFlow.Render);
                var index = candidates.FindIndex(c => c.Id == current);
                var next = candidates[(index + 1) % candidates.Count];
                if (next.Id == current)
                {
                    Show(context, "", $"Only one output selected: {next.Name}");
                    return;
                }

                if (AudioDevices.SetDefault(next.Id))
                {
                    context.Log.Info($"Default output -> {next.Name}");
                    Show(context, "", next.Name);
                }
                else
                {
                    context.Notifier.Show("Sound output", $"Could not switch to {next.Name}.", NotificationLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                context.Log.Error("Output switch failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private void Show(IFeatureContext context, string glyph, string text)
    {
        context.Ui.BeginInvoke(() =>
        {
            if (_context is null)
            {
                return;
            }

            _osd ??= new Osd();
            _osd.Display(glyph, text);
        });
    }
}
