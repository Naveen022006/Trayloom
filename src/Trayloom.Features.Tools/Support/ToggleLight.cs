// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Windows.Devices.Enumeration;
using Windows.Devices.Lights;

namespace Trayloom.Features.Tools;

/// <summary>
/// The keyboard backlight through Dynamic Lighting (<c>LampArray</c>). Windows has no generic backlight API: when no
/// controllable keyboard lamp array exists the row and the Settings switch are not listed (like a Mac without a keyboard light).
/// </summary>
internal static class ToggleKeyboardLight
{
    private static LampArray? _array;
    private static int _probing;

    /// <summary>Null until the probe finished, then whether a controllable keyboard exists.</summary>
    public static bool? Present { get; private set; }

    /// <summary>Raised on a thread-pool thread when the probe finished.</summary>
    public static event Action? Probed;

    /// <summary>Looks for a keyboard lamp array (once).</summary>
    public static async Task ProbeAsync()
    {
        if (Present is not null || Interlocked.Exchange(ref _probing, 1) == 1)
        {
            return;
        }

        try
        {
            var infos = await DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector());
            foreach (var info in infos)
            {
                var array = await LampArray.FromIdAsync(info.Id);
                if (array is not null && array.LampArrayKind == LampArrayKind.Keyboard)
                {
                    _array = array;
                    break;
                }
            }
        }
        catch
        {
            _array = null;
        }

        Present = _array is not null;
        Volatile.Write(ref _probing, 0);
        Probed?.Invoke();
    }

    /// <summary>Brightness 0..1 (0 when disabled).</summary>
    public static double Level
    {
        get
        {
            try
            {
                return _array is null || !_array.IsEnabled ? 0 : _array.BrightnessLevel;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Sets the level; 0 switches the lamp array off.</summary>
    public static bool SetLevel(double level)
    {
        if (_array is null)
        {
            return false;
        }

        try
        {
            level = Math.Clamp(level, 0, 1);
            _array.IsEnabled = level > 0;
            if (level > 0)
            {
                _array.BrightnessLevel = level;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
