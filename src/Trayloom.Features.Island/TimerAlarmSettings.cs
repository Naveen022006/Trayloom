// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>The alarm rows of Settings > Dynamic Island > Content > Timer (spec 10a).</summary>
internal static class TimerAlarmSettings
{
    /// <summary>Adds the master switch and, while it is on, the sound, repeat and volume rows.</summary>
    public static void Add(SettingsCard card, IAppSettings app, IFeatureRuntime rt, Action rebuild)
    {
        var available = rt.IsAvailable("notch-timer");
        card.Add(SUi.SwitchRow(app, K.TimerSound, G.Ringer, Loc.T("Alarm when a timer ends"), Loc.T("Rings when a timer or a Pomodoro focus or break ends. The stopwatch never rings."), _ => rebuild(), available));
        if (!app.Get<bool>(K.TimerSound))
        {
            return;
        }

        card.AddDivider();
        var sounds = AlarmSounds.Available();
        var current = AlarmSounds.Resolve(app.Get<string>(K.TimerAlarmSound), File.Exists);
        var combo = new ComboBox { MinWidth = 160, ItemsSource = sounds.Select(s => s.Label).ToList(), IsEnabled = available };
        var index = sounds.ToList().FindIndex(s => s.Id == current);
        combo.SelectedIndex = index < 0 ? 0 : index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                app.Set(K.TimerAlarmSound, sounds[combo.SelectedIndex].Id);
            }
        };
        var preview = UiKit.Button(Loc.T("Preview"), ButtonKind.Bordered, 12, null, () =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            // The settings window is on the UI thread; the preview has its own player, so a ringing alarm is never cut off by it.
            var log = IslandCore.Current?.Ctx.Log;
            if (log is not null)
            {
                TimerAlarm.Preview(sounds[combo.SelectedIndex].Id, TimerAlarmRules.Volume(app.Get<int>(K.TimerAlarmVolume)), log);
            }
        });
        preview.Margin = new Thickness(8, 0, 0, 0);
        preview.ToolTip = Loc.T("Play this sound");
        var accessory = new StackPanel { Orientation = Orientation.Horizontal };
        accessory.Children.Add(combo);
        accessory.Children.Add(preview);
        card.Add(new SettingsRow("", Loc.T("Sound"), null, accessory));
        card.AddDivider();
        card.Add(SUi.SwitchRow(app, K.TimerAlarmRepeat, "", Loc.T("Repeat until I stop it"), Loc.T("Stops by itself after 1 minute."), null, available));
        card.AddDivider();
        card.Add(SUi.SliderLine(Loc.T("Volume"), 0, 100, Math.Clamp(app.Get<int>(K.TimerAlarmVolume), 0, 100), 5, v => Loc.F("{0}%", (int)v), v => app.Set(K.TimerAlarmVolume, (int)v), 12));
        card.AddCaption(Loc.T("Windows doesn’t tell apps whether Do not disturb is on, so the alarm always rings. Its volume follows the Windows volume."));
    }
}
