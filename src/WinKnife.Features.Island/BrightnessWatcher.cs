// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinKnife.Features.Island;

/// <summary>
/// Tells the island when the built-in panel's brightness changes, whoever changed it (brightness keys, Windows quick settings,
/// the island slider). Windows announces it as a power-setting change (GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS) to a registered
/// window; a slow background read covers machines that never send it. Create and dispose on the UI thread.
/// </summary>
internal sealed class BrightnessWatcher : IDisposable
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private static Guid _brightnessSetting = new("8ffee2c6-2d01-46be-adb9-398addc5b4ff");

    private readonly HwndSource _window;
    private readonly nint _registration;
    private readonly Timer _poll;
    private readonly Action<int> _changed;
    private int _last = -1;
    private DateTime _lastEvent = DateTime.MinValue;

    public BrightnessWatcher(Action<int> changed)
    {
        _changed = changed;
        _window = new HwndSource(new HwndSourceParameters("WinKnifeBrightnessWatcher") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(Hook);
        _registration = RegisterPowerSettingNotification(_window.Handle, ref _brightnessSetting, 0);
        _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _poll.Dispose();
        if (_registration != 0)
        {
            UnregisterPowerSettingNotification(_registration);
        }

        _window.RemoveHook(Hook);
        _window.Dispose();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmPowerBroadcast && wParam == PbtPowerSettingChange && lParam != 0)
        {
            var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
            if (setting.PowerSetting == _brightnessSetting && setting.DataLength >= 4)
            {
                var value = Marshal.ReadInt32(lParam + Marshal.OffsetOf<PowerBroadcastSetting>(nameof(PowerBroadcastSetting.Data)));
                _lastEvent = DateTime.UtcNow;
                Report(value);
            }
        }

        return 0;
    }

    /// <summary>Fallback for drivers that do not announce changes; skipped while announcements arrive.</summary>
    private void Poll()
    {
        if (DateTime.UtcNow - _lastEvent < TimeSpan.FromSeconds(30))
        {
            return;
        }

        if (BrightnessService.Read() is { } level)
        {
            var value = (int)Math.Round(level * 100);
            _window.Dispatcher.BeginInvoke(() => Report(value));
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
        if (!first)
        {
            _changed(value);
        }
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
