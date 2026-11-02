// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Prevents the PC from sleeping (and optionally keeps the display on), indefinitely or for a set time.</summary>
public sealed class KeepAwakeFeature : IFeature
{
    private IFeatureContext? _context;
    private DispatcherTimer? _timer;
    private TextBlock? _status;
    private CheckBox? _toggle;
    private bool _active;
    private DateTime _endsAt;

    public string Id => "keep-awake";

    public string Name => "Keep awake";

    public string Description => "Stop your PC from sleeping or turning off the screen, for as long as you need.";

    public FeatureGroup Group => FeatureGroup.EnergyDisplay;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", _active ? "Keep awake: on" : "Keep awake: off", Toggle, ""),
        new FeatureAction("15m", "Keep awake 15 min", () => ActivateFor(15), ""),
        new FeatureAction("1h", "Keep awake 1 hour", () => ActivateFor(60), ""),
        new FeatureAction("2h", "Keep awake 2 hours", () => ActivateFor(120), ""),
        new FeatureAction("forever", "Keep awake until off", () => ActivateFor(0), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        context.Settings.Changed += OnSettingChanged;
    }

    public void Stop()
    {
        Deactivate();
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        _context = null;
        _status = null;
        _toggle = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        var toggle = new CheckBox { IsChecked = _active };
        _toggle = toggle;
        toggle.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.SwitchStyle);
        toggle.Click += (_, _) =>
        {
            if ((toggle.IsChecked == true) != _active)
            {
                Toggle();
            }
        };

        _status = new TextBlock { Margin = new Thickness(2, 8, 2, 0) };
        _status.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        UpdateStatus();

        var panel = new StackPanel();
        panel.Children.Add(new SettingsCard("Keep this PC awake", "Turns on immediately with the duration below; 15 min, 1 h and 2 h shortcuts are in the quick panel.", toggle));
        var settings = new SettingsViewBuilder(_context.Settings)
            .AddChoice(
                "minutes",
                "Turn off automatically after",
                new Choice<int>[]
                {
                    new(0, "Until I turn it off"),
                    new(15, "15 minutes"),
                    new(30, "30 minutes"),
                    new(60, "1 hour"),
                    new(120, "2 hours"),
                    new(240, "4 hours"),
                    new(480, "8 hours"),
                },
                0)
            .AddToggle("keepDisplayOn", "Also keep the display on", true)
            .Build();
        settings.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(settings);
        panel.Children.Add(_status);
        return panel;
    }

    private void Toggle()
    {
        if (_context is null)
        {
            return;
        }

        if (_active)
        {
            Deactivate();
        }
        else
        {
            Activate(_context.Settings.Get("minutes", 0));
        }
    }

    /// <summary>Starts (or restarts) keep awake with an explicit duration; 0 means until turned off.</summary>
    private void ActivateFor(int minutes)
    {
        if (_context is null)
        {
            return;
        }

        if (_active)
        {
            Deactivate();
        }

        Activate(minutes);
    }

    private void Activate(int minutes)
    {
        var keepDisplay = _context!.Settings.Get("keepDisplayOn", true);
        ApplyExecutionState(keepDisplay);
        _active = true;

        if (minutes > 0)
        {
            _endsAt = DateTime.Now.AddMinutes(minutes);
            _timer?.Stop();
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, _context.Ui);
            _timer.Start();
        }

        _context.Log.Info($"Keep awake on (display: {keepDisplay}, minutes: {minutes})");
        _toggle?.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        UpdateStatus();
    }

    private void Deactivate()
    {
        _timer?.Stop();
        _timer = null;
        if (_active)
        {
            // Clearing the state hands control back to the system. Runs on the thread that set it (the UI thread).
            NativeMethods.SetThreadExecutionState(NativeConstants.ES_CONTINUOUS);
            _active = false;
            _context?.Log.Info("Keep awake off");
        }

        _toggle?.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, _active);
        UpdateStatus();
    }

    private static void ApplyExecutionState(bool keepDisplay)
    {
        var flags = NativeConstants.ES_CONTINUOUS | NativeConstants.ES_SYSTEM_REQUIRED;
        if (keepDisplay)
        {
            flags |= NativeConstants.ES_DISPLAY_REQUIRED;
        }

        if (NativeMethods.SetThreadExecutionState(flags) == 0)
        {
            throw new InvalidOperationException("SetThreadExecutionState failed.");
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (DateTime.Now >= _endsAt)
        {
            Deactivate();
            _context?.Notifier.Show("Keep awake", "Keep awake turned off - your PC can sleep again.");
        }
        else
        {
            UpdateStatus();
        }
    }

    private void OnSettingChanged(string key)
    {
        // Re-apply the display flag live if it is toggled while active.
        if (key == "keepDisplayOn" && _active && _context is not null)
        {
            _context.Ui.BeginInvoke(() =>
            {
                if (_active && _context is not null)
                {
                    ApplyExecutionState(_context.Settings.Get("keepDisplayOn", true));
                }
            });
        }
    }

    private void UpdateStatus()
    {
        if (_status is null)
        {
            return;
        }

        _status.Text = !_active
            ? "Keep awake is off."
            : _timer is null
                ? "Keeping this PC awake until you turn it off."
                : $"Keeping this PC awake for {Math.Max(0, (int)Math.Ceiling((_endsAt - DateTime.Now).TotalMinutes))} more minute(s).";
    }
}
