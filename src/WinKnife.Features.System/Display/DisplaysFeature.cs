// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Brightness sliders for built-in panels (WMI) and external monitors (DDC/CI), plus optional brightness hotkeys.</summary>
public sealed class DisplaysFeature : IFeature
{
    private IFeatureContext? _context;
    private HotkeyBinding? _upHotkey;
    private HotkeyBinding? _downHotkey;
    private DisplaysWindow? _window;
    private Osd? _osd;
    private Task<List<DisplayDevice>>? _devices;

    public string Id => "displays";

    public string Name => "Displays";

    public string Description => "Change the brightness of your built-in screen and compatible external monitors, from a flyout or with hotkeys.";

    public FeatureGroup Group => FeatureGroup.EnergyDisplay;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("panel", "Display brightness", TogglePanel, ""),
        new FeatureAction("brighter", "Brightness up", () => Adjust(+1), ""),
        new FeatureAction("dimmer", "Brightness down", () => Adjust(-1), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _upHotkey = new HotkeyBinding(context, "upHotkey", null, () => Adjust(+1));
        _downHotkey = new HotkeyBinding(context, "downHotkey", null, () => Adjust(-1));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Stop()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _upHotkey?.Dispose();
        _upHotkey = null;
        _downHotkey?.Dispose();
        _downHotkey = null;
        if (_window is not null)
        {
            _window.Hide();
            _window.Close();
            _window = null;
        }

        _osd?.Close();
        _osd = null;
        InvalidateDevices();
        _context = null;
    }

    public FrameworkElement? CreateSettingsView() =>
        _context is null
            ? null
            : new SettingsViewBuilder(_context.Settings)
                .AddHotkey("upHotkey", "Brightness up", null, "Adjusts the display under the mouse pointer.")
                .AddHotkey("downHotkey", "Brightness down", null)
                .AddIntSlider("step", "Hotkey step (percent)", 2, 25, 10)
                .AddNote("Built-in laptop screens use Windows WMI; external monitors need DDC/CI enabled in their on-screen menu. Displays that cannot be controlled are hidden.")
                .Build();

    /// <summary>Detects the adjustable displays once and caches them until the display configuration changes.</summary>
    internal Task<List<DisplayDevice>> GetDevicesAsync()
    {
        return _devices ??= Task.Run(() =>
        {
            try
            {
                return DisplayNative.Enumerate();
            }
            catch (Exception ex)
            {
                _context?.Log.Error("Display detection failed", ex);
                return [];
            }
        });
    }

    internal void Refresh()
    {
        InvalidateDevices();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _context?.Ui.BeginInvoke(InvalidateDevices);

    private void InvalidateDevices()
    {
        var old = _devices;
        _devices = null;
        if (old is not null)
        {
            // Wait for any in-flight detection, then free the monitor handles.
            _ = old.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    foreach (var device in t.Result)
                    {
                        device.Dispose();
                    }
                }
            });
        }
    }

    private void TogglePanel()
    {
        if (_context is null)
        {
            return;
        }

        _window ??= new DisplaysWindow(this);
        _window.Toggle();
    }

    private async void Adjust(int direction)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        try
        {
            var devices = await GetDevicesAsync();
            if (devices.Count == 0)
            {
                context.Notifier.Show("Displays", "No adjustable displays found.", NotificationLevel.Warning);
                return;
            }

            var monitor = NativeMethods.MonitorFromPoint(
                new POINT { X = (int)Screens.CursorPosition().X, Y = (int)Screens.CursorPosition().Y },
                NativeConstants.MONITOR_DEFAULTTONEAREST);
            var targets = devices.Where(d => d.HMonitor == monitor).ToList();
            if (targets.Count == 0)
            {
                targets = [devices[0]];
            }

            var step = context.Settings.Get("step", 10);
            var first = targets[0];
            var value = Math.Clamp(first.Percent + (direction * step), 0, 100);
            var ok = await Task.Run(() => targets.Aggregate(true, (acc, target) => target.Write(value) && acc));
            if (!ok)
            {
                context.Log.Warn($"Setting brightness on {first.Name} failed.");
            }

            if (_context is not null)
            {
                _osd ??= new Osd();
                _osd.Display("", $"{first.Name}: {value}%");
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Brightness hotkey failed", ex);
        }
    }
}

/// <summary>The brightness flyout: one slider per adjustable display.</summary>
internal sealed class DisplaysWindow : FlyoutWindow
{
    private readonly DisplaysFeature _feature;
    private readonly StackPanel _rows = new();
    private int _generation;

    public DisplaysWindow(DisplaysFeature feature)
        : base("Display brightness", "", 380)
    {
        _feature = feature;
        Body.Children.Add(_rows);
        var refresh = new Button { Content = "", ToolTip = "Detect displays again", Width = 30, Height = 30, Padding = new Thickness(0), FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
        refresh.Click += (_, _) =>
        {
            _feature.Refresh();
            Populate();
        };
        HeaderExtra.Children.Add(refresh);
    }

    protected override void OnShown() => Populate();

    private void Populate()
    {
        var generation = ++_generation;
        _rows.Children.Clear();
        _rows.Children.Add(Message("Detecting displays..."));
        _ = LoadAsync(generation);
    }

    private async Task LoadAsync(int generation)
    {
        List<DisplayDevice> devices;
        try
        {
            devices = await _feature.GetDevicesAsync();
        }
        catch (Exception)
        {
            devices = [];
        }

        if (generation != _generation)
        {
            return;
        }

        _rows.Children.Clear();
        if (devices.Count == 0)
        {
            _rows.Children.Add(Message("No adjustable displays found. External monitors need DDC/CI enabled in their on-screen menu."));
            return;
        }

        foreach (var device in devices)
        {
            _rows.Children.Add(BuildRow(device));
        }
    }

    private static TextBlock Message(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(6, 8, 6, 8) };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        return block;
    }

    private UIElement BuildRow(DisplayDevice device)
    {
        var name = new TextBlock { Text = device.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        var kind = new TextBlock { Text = device.Kind, FontSize = 11 };
        kind.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);

        var glyph = new TextBlock { Text = "", FontSize = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.IconTextStyle);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = device.Percent, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        var percent = new TextBlock { Text = $"{device.Percent}%", Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        percent.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);

        // DDC/CI writes are slow: coalesce slider movement and write on a worker thread.
        var pending = -1;
        var writing = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += async (_, _) =>
        {
            if (writing || pending < 0)
            {
                return;
            }

            writing = true;
            var value = pending;
            pending = -1;
            await Task.Run(() => device.Write(value));
            writing = false;
        };
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{(int)e.NewValue}%";
            pending = (int)e.NewValue;
            if (!timer.IsEnabled)
            {
                timer.Start();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                timer.Stop();
            }
        };

        var sliderRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        sliderRow.Children.Add(glyph);
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(percent);

        var stack = new StackPanel();
        stack.Children.Add(name);
        stack.Children.Add(kind);
        stack.Children.Add(sliderRow);
        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = stack };
        card.SetResourceReference(Border.StyleProperty, ThemeKeys.CardStyle);
        return card;
    }
}
