// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>A display as shown in the panel and in Settings.</summary>
internal sealed class DisplayItem
{
    public required DisplayInfo Info { get; set; }

    /// <summary>Hardware control (WMI for the built-in panel, DDC/CI for external monitors); null = only software dimming exists.</summary>
    public DisplayDevice? Device { get; set; }

    /// <summary>The software-dimming level chosen for this display (100 = no dimming).</summary>
    public int SoftwareLevel { get; set; } = 100;

    public bool SoftwareChosen { get; set; }

    public bool Pending { get; set; }

    /// <summary>The level asked for while a hardware write is pending (-1 = none).</summary>
    public int RequestedLevel { get; set; } = -1;

    public string? Failure { get; set; }

    public string Key => Info.Key;

    public string Name => Info.Name;

    public bool IsBuiltIn => Info.IsBuiltIn;

    public bool IsActive => Info.IsActive;

    /// <summary>True when the hardware brightness can be read (DDC readable / WMI).</summary>
    public bool HardwareControl => Device is not null;

    /// <summary>The "Extra dimming" / "Dim the picture" button is offered for active external displays.</summary>
    public bool CanChooseDimming => IsActive && !IsBuiltIn;

    /// <summary>The slider value (0..100): software level when software dimming is chosen or the only option, else the hardware level.</summary>
    public int Level => SoftwareChosen || Device is null ? SoftwareLevel : Pending && RequestedLevel >= 0 ? RequestedLevel : Device.Percent;
}

/// <summary>
/// Display brightness service (spec 05 E2): detects displays, writes hardware brightness on a serial worker with pacing and coalescing
/// (WMI for the built-in panel, DDC/CI for external monitors), falls back to a click-through dimming overlay, switches displays on and off
/// and shows the brightness OSD. Created by the Displays feature; UI thread only unless noted.
/// </summary>
internal sealed class DisplayService : IDisposable
{
    private readonly IFeatureContext _ctx;
    private readonly ConcurrentDictionary<string, int> _pending = new();
    private readonly Dictionary<string, DimOverlay> _overlays = [];
    private List<DisplayItem> _items = [];
    private Task<List<DisplayItem>>? _detect;
    private int _worker;
    private Osd? _osd;
    private bool _disposed;

    public DisplayService(IFeatureContext ctx)
    {
        _ctx = ctx;
        SystemEvents.DisplaySettingsChanged += OnTopology;
        SystemEvents.PowerModeChanged += OnPower;
    }

    public static DisplayService? Instance { get; private set; }

    /// <summary>Raised on the UI thread when the display list or a display state changed.</summary>
    public event Action? Changed;

    public IReadOnlyList<DisplayItem> Items => _items;

    public void Start()
    {
        Instance = this;
        _ = ItemsAsync(rescan: true);
    }

    public void Stop()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnTopology;
        SystemEvents.PowerModeChanged -= OnPower;
        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
        }

        _overlays.Clear();
        _osd?.Close();
        _osd = null;
        var old = _items;
        _items = [];
        foreach (var item in old)
        {
            item.Device?.Dispose();
        }
    }

    /// <summary>Detects the displays (cached; <paramref name="rescan"/> forces a new detection).</summary>
    public async Task<List<DisplayItem>> ItemsAsync(bool rescan = false)
    {
        if (_disposed)
        {
            return [];
        }

        if (rescan || _detect is null)
        {
            _detect = Task.Run(Detect);
        }

        var detect = _detect;
        List<DisplayItem> result;
        try
        {
            result = await detect;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Display detection failed", ex);
            result = [];
        }

        if (_disposed || detect != _detect)
        {
            return result;
        }

        var old = _items;
        _items = result;
        foreach (var item in old.Where(o => result.All(r => !ReferenceEquals(r.Device, o.Device))))
        {
            item.Device?.Dispose();
        }

        ApplyOverlays();
        Changed?.Invoke();
        return result;
    }

    private List<DisplayItem> Detect()
    {
        var topology = DisplayTopology.Enumerate();
        var hardware = DisplayNative.Enumerate();
        var preferred = _ctx.App.Get<List<string>>("softwareDimmingPreferred");
        var levels = _ctx.App.Get<Dictionary<string, int>>("softwareDimLevels");
        var items = new List<DisplayItem>();
        foreach (var info in topology)
        {
            var device = hardware.FirstOrDefault(h => !string.IsNullOrEmpty(info.GdiName) && h.Id.StartsWith(info.GdiName, StringComparison.OrdinalIgnoreCase));
            items.Add(new DisplayItem
            {
                Info = info,
                Device = device,
                SoftwareChosen = preferred.Contains(info.Key) || (device is null && !info.IsBuiltIn && info.IsActive),
                SoftwareLevel = levels.TryGetValue(info.Key, out var l) ? Math.Clamp(l, 5, 100) : 100,
            });
        }

        if (items.Count == 0)
        {
            // Topology unavailable: show whatever hardware control exists.
            foreach (var h in hardware)
            {
                items.Add(new DisplayItem { Info = new DisplayInfo(h.Id, h.Name, h.IsInternal, true, 0, 0) { }, Device = h });
            }
        }

        foreach (var h in hardware.Where(h => items.All(i => !ReferenceEquals(i.Device, h))))
        {
            h.Dispose();
        }

        return items;
    }

    private void OnTopology(object? sender, EventArgs e) => _ctx.Ui.BeginInvoke(() => _ = ItemsAsync(rescan: true));

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ctx.Ui.BeginInvoke(() => _ = ItemsAsync(rescan: true));
        }
    }

    // ------------------------------------------------------------------------------------------------ brightness

    /// <summary>Sets the level (0..100) of a display: hardware when available and not overridden, else the dimming overlay.</summary>
    public void SetLevel(DisplayItem item, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (!item.IsActive)
        {
            return;
        }

        if (item.SoftwareChosen || item.Device is null)
        {
            item.SoftwareLevel = Math.Clamp(percent, 5, 100);
            PersistSoftware(item);
            ApplyOverlay(item);
        }
        else
        {
            item.RequestedLevel = percent;
            _pending[item.Key] = percent;
            item.Pending = true;
            StartWorker(item);
        }

        ShowOsd(item, percent);
    }

    /// <summary>Chooses (or leaves) software dimming for an external display. Leaving restores full hardware level.</summary>
    public void SetSoftware(DisplayItem item, bool chosen)
    {
        item.SoftwareChosen = chosen;
        var list = _ctx.App.Get<List<string>>("softwareDimmingPreferred");
        list.Remove(item.Key);
        if (chosen)
        {
            list.Add(item.Key);
        }
        else
        {
            item.SoftwareLevel = 100;
        }

        _ctx.App.Set("softwareDimmingPreferred", list);
        PersistSoftware(item);
        ApplyOverlay(item);
        Changed?.Invoke();
    }

    private void PersistSoftware(DisplayItem item)
    {
        var levels = _ctx.App.Get<Dictionary<string, int>>("softwareDimLevels");
        if (item.SoftwareLevel >= 100)
        {
            levels.Remove(item.Key);
        }
        else
        {
            levels[item.Key] = item.SoftwareLevel;
        }

        _ctx.App.Set("softwareDimLevels", levels);
    }

    private void StartWorker(DisplayItem item)
    {
        if (Interlocked.Exchange(ref _worker, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                while (!_pending.IsEmpty)
                {
                    foreach (var key in _pending.Keys.ToList())
                    {
                        if (!_pending.TryRemove(key, out var percent))
                        {
                            continue;
                        }

                        var target = _items.FirstOrDefault(i => i.Key == key);
                        var ok = target?.Device?.Write(percent) ?? false;
                        if (target is not null)
                        {
                            target.Pending = _pending.ContainsKey(key);
                            target.Failure = ok ? null : Strings.Displays.ChangeFailed;
                        }

                        Thread.Sleep(ok ? 25 : 60);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _worker, 0);
                _ctx.Ui.BeginInvoke(() => Changed?.Invoke());
            }
        });
        _ = item;
    }

    /// <summary>Applies a step (+1 / -1 times the key step) to the display under the pointer or the primary display.</summary>
    public async void Step(int direction, bool followPointer)
    {
        try
        {
            var items = _items.Count > 0 ? _items : await ItemsAsync();
            var active = items.Where(i => i.IsActive).ToList();
            if (active.Count == 0)
            {
                return;
            }

            DisplayItem? target = null;
            if (followPointer)
            {
                var p = Screens.CursorPosition();
                var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)p.X, Y = (int)p.Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
                target = active.FirstOrDefault(i => i.Device is { } d && d.HMonitor == monitor);
            }

            target ??= active.FirstOrDefault(i => i.IsBuiltIn) ?? active[0];
            var baseStep = _ctx.App.Get<string>("brightnessKeyStep") switch { "half" => 5.0, "quarter" => 2.5, _ => 10.0 };
            var step = Math.Max(1, (int)Math.Round(baseStep));
            var next = Math.Clamp(target.Level + (direction * step), 0, 100);
            SetLevel(target, next);
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Brightness shortcut failed", ex);
        }
    }

    private void ShowOsd(DisplayItem item, int percent)
    {
        if (!_ctx.App.Get<bool>("brightnessOSDEnabled"))
        {
            return;
        }

        _osd ??= new Osd();
        _osd.Display(Gl.Sun, $"{item.Name}: {percent}%", 1500);
    }

    // ------------------------------------------------------------------------------------------------ overlays

    private void ApplyOverlays()
    {
        foreach (var item in _items)
        {
            ApplyOverlay(item);
        }

        foreach (var key in _overlays.Keys.Where(k => _items.All(i => i.Key != k)).ToList())
        {
            _overlays[key].Close();
            _overlays.Remove(key);
        }
    }

    private void ApplyOverlay(DisplayItem item)
    {
        var want = item.IsActive && (item.SoftwareChosen || item.Device is null) && item.SoftwareLevel < 100;
        if (!want)
        {
            if (_overlays.Remove(item.Key, out var gone))
            {
                gone.Close();
            }

            return;
        }

        if (!_overlays.TryGetValue(item.Key, out var overlay))
        {
            overlay = new DimOverlay();
            _overlays[item.Key] = overlay;
        }

        overlay.Present(item.Info.GdiName, 1.0 - (item.SoftwareLevel / 100.0));
    }

    // ------------------------------------------------------------------------------------------------ power

    /// <summary>Switches a display on or off; returns the failure text or null.</summary>
    public async Task<string?> SetPowerAsync(DisplayItem item, bool on)
    {
        if (!on && _items.Count(i => i.IsActive) <= 1)
        {
            return Strings.Displays.LastDisplay;
        }

        item.Pending = true;
        Changed?.Invoke();
        var ok = await Task.Run(() => on ? DisplayTopology.SwitchOn() : DisplayTopology.SwitchOff(item.Info));
        item.Pending = false;
        if (!ok)
        {
            Changed?.Invoke();
            return item.IsBuiltIn && on ? Strings.Displays.OpenLid : Strings.Displays.SwitchingUnavailable;
        }

        await Task.Delay(700);
        await ItemsAsync(rescan: true);
        return null;
    }
}

/// <summary>A click-through black layer over one monitor that dims the picture (alpha = how much to dim).</summary>
internal sealed class DimOverlay : OverlayWindow
{
    private readonly System.Windows.Controls.Border _layer = new() { Background = Brushes.Black };

    public DimOverlay()
    {
        ClickThrough = true;
        Content = _layer;
        Width = 10;
        Height = 10;
        IsHitTestVisible = false;
    }

    /// <summary>Shows the layer over the monitor with the GDI name; <paramref name="dim"/> is 0..0.95.</summary>
    public void Present(string gdiName, double dim)
    {
        var monitor = string.IsNullOrEmpty(gdiName) ? null : DisplayNative.FindMonitor(gdiName);
        if (monitor is null)
        {
            Hide();
            return;
        }

        _layer.Opacity = Math.Clamp(dim, 0, 0.95);
        if (!IsVisible)
        {
            ShowNoActivate();
        }

        var b = monitor.Value.Bounds;
        SetPhysicalBounds(b.Left, b.Top, b.Width, b.Height);
    }
}
