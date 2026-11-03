// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinKnife.Core;

/// <summary>
/// Marks the moments WinKnife itself changes the built-in panel's brightness (island slider, tray panel, shortcuts, lid dimming,
/// command bar), so the island's "Brightness" notice only appears for changes made by something else (keys, Windows quick settings).
/// </summary>
public static class BrightnessWrites
{
    private static long _ticks;

    /// <summary>When WinKnife last wrote the panel brightness (UTC; <see cref="DateTime.MinValue"/> before the first write).</summary>
    public static DateTime LastOwnWrite => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

    /// <summary>Call right before (and after) every brightness write WinKnife makes. Any thread.</summary>
    public static void MarkOwn() => Interlocked.Exchange(ref _ticks, DateTime.UtcNow.Ticks);

    /// <summary>True when WinKnife wrote the brightness less than <paramref name="within"/> ago.</summary>
    public static bool WroteWithin(TimeSpan within) => DateTime.UtcNow - LastOwnWrite < within;
}

/// <summary>
/// Tells its owner when the built-in panel's brightness changes, whoever changed it (brightness keys, Windows quick settings,
/// WinKnife's own sliders). Windows announces it as a power-setting change (GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS) to a registered
/// window; an optional slow background read covers machines that never send it. The display state (on, off, dimmed) is tracked too,
/// so the dimming Windows does on idle or battery saver is reported without asking for a notice. Create and dispose on the UI thread.
/// </summary>
public sealed class BrightnessWatcher : IDisposable
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleAfterWake = TimeSpan.FromSeconds(2);
    private static Guid _brightnessSetting = new("8ffee2c6-2d01-46be-adb9-398addc5b4ff");
    private static Guid _displayStateSetting = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private readonly HwndSource _window;
    private readonly nint _registration;
    private readonly nint _stateRegistration;
    private readonly Timer? _poll;
    private readonly Action<int, bool> _changed;
    private readonly Func<int?>? _fallbackRead;
    private int _last = -1;
    private int _eventCount;
    private int _polling;
    private bool _eventsSupported;
    private bool _disposed;

    // GUID_CONSOLE_DISPLAY_STATE: 0 off, 1 on, 2 dimmed (idle or battery saver). Touched on the UI thread only.
    private int _displayState = 1;
    private DateTime _displayOnAt = DateTime.MinValue;

    /// <summary>
    /// <paramref name="changed"/> receives the level (0..100) and whether a notice is appropriate (the display is on and has not just
    /// woken or stopped dimming). The first level seen after start is reported too, without a notice.
    /// <paramref name="fallbackRead"/> (optional, called on a worker thread) reads the level for drivers that never announce changes.
    /// </summary>
    public BrightnessWatcher(Action<int, bool> changed, Func<int?>? fallbackRead = null)
    {
        _changed = changed;
        _fallbackRead = fallbackRead;
        _window = new HwndSource(new HwndSourceParameters("WinKnifeBrightnessWatcher") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(Hook);
        _registration = RegisterPowerSettingNotification(_window.Handle, ref _brightnessSetting, 0);
        _stateRegistration = RegisterPowerSettingNotification(_window.Handle, ref _displayStateSetting, 0);
        if (fallbackRead is not null)
        {
            _poll = new Timer(_ => Poll(), null, FastPoll, FastPoll);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _poll?.Dispose();
        if (_registration != 0)
        {
            UnregisterPowerSettingNotification(_registration);
        }

        if (_stateRegistration != 0)
        {
            UnregisterPowerSettingNotification(_stateRegistration);
        }

        _window.RemoveHook(Hook);
        _window.Dispose();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmPowerBroadcast && wParam == PbtPowerSettingChange && lParam != 0)
        {
            var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
            if (setting.DataLength >= 4)
            {
                var value = Marshal.ReadInt32(lParam + Marshal.OffsetOf<PowerBroadcastSetting>(nameof(PowerBroadcastSetting.Data)));
                if (setting.PowerSetting == _brightnessSetting)
                {
                    Interlocked.Increment(ref _eventCount);
                    SawEvent();
                    Report(value);
                }
                else if (setting.PowerSetting == _displayStateSetting)
                {
                    if (value == 1 && _displayState != 1)
                    {
                        _displayOnAt = DateTime.UtcNow;
                    }

                    _displayState = value;
                }
            }
        }

        return 0;
    }

    /// <summary>This driver announces changes: the fallback read slows down to a rare safety net.</summary>
    private void SawEvent()
    {
        if (_eventsSupported)
        {
            return;
        }

        _eventsSupported = true;
        try
        {
            _poll?.Change(SlowPoll, SlowPoll);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Fallback for drivers that do not announce changes (every 2 s until one announcement arrives, then every 30 s).</summary>
    private void Poll()
    {
        if (_fallbackRead is null || _disposed || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var seen = Volatile.Read(ref _eventCount);
            if (_fallbackRead() is { } value)
            {
                // an announcement that arrived while the read ran is newer than this value
                _window.Dispatcher.BeginInvoke(() =>
                {
                    if (!_disposed && Volatile.Read(ref _eventCount) == seen)
                    {
                        Report(value);
                    }
                });
            }
        }
        catch
        {
            // a failed read is retried on the next tick
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Report(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (value == _last)
        {
            return;
        }

        var first = _last < 0;
        _last = value;
        var notice = !first && _displayState == 1 && DateTime.UtcNow - _displayOnAt > SettleAfterWake;
        _changed(value, notice);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public int DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
}
