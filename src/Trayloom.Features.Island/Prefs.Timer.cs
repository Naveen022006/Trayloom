// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Settings keys of the timer alarm (spec 10a). The master switch is the existing <see cref="TimerSound"/>.</summary>
internal static partial class K
{
    /// <summary>Which alarm sound rings: "classic", "gentle" or "alarm01".."alarm10".</summary>
    public const string TimerAlarmSound = "notchTimerAlarmSound";

    /// <summary>Keep ringing until stopped (at most a minute) instead of playing once.</summary>
    public const string TimerAlarmRepeat = "notchTimerAlarmRepeat";

    /// <summary>Alarm volume, 0 to 100, on top of the Windows volume.</summary>
    public const string TimerAlarmVolume = "notchTimerAlarmVolume";

    /// <summary>Registers the defaults of the timer alarm.</summary>
    private static void RegisterTimerAlarmDefaults(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [TimerAlarmSound] = AlarmSounds.Default,
            [TimerAlarmRepeat] = true,
            [TimerAlarmVolume] = 70,
        });
    }
}
