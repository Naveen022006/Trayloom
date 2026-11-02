// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>Raises the island notices of spec 7.7 from system events (volume, microphone, input source, battery, track, timer, unlock).</summary>
internal sealed class NoticeSources : IDisposable
{
    private readonly IslandCore _core;
    private readonly DispatcherTimer _poll;
    private long _layout;
    private bool _micMuted;
    private bool _plugged;
    private bool _lowArmed = true;
    private int _lastPercent = -1;
    private bool _started;
    private DispatcherTimer? _trackTimer;
    private Playback? _pendingTrack;

    public NoticeSources(IslandCore core)
    {
        _core = core;
        _poll = new DispatcherTimer(DispatcherPriority.Background, core.Ui) { Interval = TimeSpan.FromMilliseconds(500) };
        _poll.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _micMuted = _core.Audio.MicMuted;
        var (has, pct, plugged, _) = IslandNative.Power();
        _plugged = plugged;
        _lastPercent = has ? pct : -1;
        _layout = IslandNative.ForegroundLayout();
        _core.Audio.VolumeChanged += OnVolume;
        _core.Audio.MicChanged += OnMic;
        _core.Playback.TrackChanged += OnTrack;
        _core.Timer.Finished += OnTimerFinished;
        _core.Session.Unlocked += OnUnlock;
        _core.Session.Locking += OnLock;
        SystemEvents.PowerModeChanged += OnPower;
        _poll.Start();
        _volume = _core.Audio.Volume ?? 0;
        _muted = _core.Audio.Muted;
    }

    private double _volume;
    private bool _muted;

    private void OnVolume()
    {
        var v = _core.Audio.Volume ?? 0;
        var m = _core.Audio.Muted;
        var changed = Math.Abs(v - _volume) > 0.004 || m != _muted;
        _volume = v;
        _muted = m;
        if (!changed || _core.Audio.RecentlyOwnChange || !_core.Ctx.Runtime.IsAvailable("mixer"))
        {
            return;
        }

        _core.ShowNotice(new Notice
        {
            Event = NoticeEvents.Volume,
            Glyph = m || v <= 0.001 ? G.SpeakerOff : G.Speaker,
            Title = Loc.T("Volume"),
            Level = m ? 0 : v,
            Detail = $"{(int)Math.Round((m ? 0 : v) * 100)}%",
        });
    }

    private void OnMic()
    {
        var m = _core.Audio.MicMuted;
        if (m == _micMuted)
        {
            return;
        }

        _micMuted = m;
        if (!_core.Ctx.Runtime.IsAvailable("mic-mute"))
        {
            return;
        }

        _core.ShowNotice(new Notice { Event = NoticeEvents.Microphone, Glyph = m ? G.MicOff : G.Mic, Title = m ? Loc.T("Mic Mute on") : Loc.T("Mic Mute off"), Detail = "", Tint = m ? Color.FromRgb(0xFF, 0x45, 0x3A) : null, Reaction = m ? "hush" : "perk" });
        _core.Mascot.React(m ? "hush" : "perk");
    }

    private void OnTrack(Playback p)
    {
        if (!_core.ModuleVisible("music"))
        {
            return;
        }

        _pendingTrack = p;
        _trackTimer ??= new DispatcherTimer(DispatcherPriority.Normal, _core.Ui) { Interval = TimeSpan.FromSeconds(0.5) };
        _trackTimer.Tick -= ShowTrack;
        _trackTimer.Tick += ShowTrack;
        _trackTimer.Stop();
        _trackTimer.Start();
    }

    private void ShowTrack(object? sender, EventArgs e)
    {
        _trackTimer?.Stop();
        var p = _core.Playback.Current;
        if (!p.HasTrack || !p.IsPlaying)
        {
            return;
        }

        _core.ShowNotice(new Notice { Event = NoticeEvents.Track, Glyph = G.Music, Title = Loc.T("New track"), Detail = p.Display, Image = p.Artwork });
    }

    private void OnTimerFinished(TimerPhase phase, bool cycle)
    {
        _core.ShowNotice(new Notice
        {
            Event = NoticeEvents.Timer,
            Glyph = G.Timer,
            Title = cycle ? Loc.T("Pomodoro complete") : Loc.T("Time is up"),
            Detail = TimerService.PhaseTitle(phase),
            Tint = Color.FromRgb(0xFF, 0x9F, 0x0A),
            Reaction = phase == TimerPhase.Focus ? "celebrate" : (phase is TimerPhase.ShortBreak or TimerPhase.LongBreak ? "ready" : "surprised"),
        });
    }

    private void OnLock()
    {
        if (_core.App.Get<bool>(K.LockSounds) && _core.Session.HearsLockChange)
        {
            _core.Session.PlayLockSound(false);
        }
    }

    private void OnUnlock()
    {
        if (_core.App.Get<bool>(K.LockSounds))
        {
            _core.Session.PlayLockSound(true);
        }

        if (_core.App.Get<bool>(K.LockScreen))
        {
            _core.Ui.BeginInvoke(async () =>
            {
                await Task.Delay(300);
                _core.ShowNotice(new Notice { Event = NoticeEvents.Unlock, Glyph = G.Unlock, Title = _core.Timer.Completed ? Loc.T("Time is up") : Loc.T("Welcome back"), Detail = "", Reaction = "wakeUp" });
            });
        }
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            _core.Ui.BeginInvoke(CheckBattery);
        }
    }

    private void Poll()
    {
        if (!_core.IslandOn)
        {
            return;
        }

        var layout = IslandNative.ForegroundLayout();
        if (layout != 0 && layout != _layout)
        {
            var first = _layout == 0;
            _layout = layout;
            if (!first && _core.App.Get<bool>(K.InputSource))
            {
                var lcid = (int)(layout & 0xFFFF);
                string name;
                try
                {
                    name = CultureInfo.GetCultureInfo(lcid).DisplayName;
                }
                catch
                {
                    name = "";
                }

                _core.ShowNotice(new Notice { Event = NoticeEvents.InputSource, Glyph = G.Keyboard, Title = Loc.T("Input source"), Detail = name });
            }
        }

        if (DateTime.Now.Second % 15 == 0)
        {
            CheckBattery();
        }
    }

    private void CheckBattery()
    {
        var (has, pct, plugged, charging) = IslandNative.Power();
        if (!has)
        {
            return;
        }

        if (plugged != _plugged && _core.Ctx.Runtime.IsAvailable("monitor-power"))
        {
            _plugged = plugged;
            _core.ShowNotice(new Notice
            {
                Event = NoticeEvents.Battery,
                Glyph = plugged ? G.BatteryCharging : G.Battery,
                Title = plugged ? (pct >= 100 ? Loc.T("Fully charged") : Loc.T("Charging")) : Loc.T("On battery"),
                Level = pct / 100.0,
                Detail = $"{pct}%",
                Reaction = plugged ? "love" : null,
            });
            _core.Mascot.React(plugged ? "love" : "yawn");
        }

        if (!plugged && pct <= 20 && _lowArmed && _lastPercent > 20 && _core.Ctx.Runtime.IsAvailable("monitor-power"))
        {
            _lowArmed = false;
            _core.ShowNotice(new Notice { Event = NoticeEvents.Battery, Glyph = G.Battery, Title = Loc.T("Low battery"), Level = pct / 100.0, Detail = $"{pct}%", Tint = Color.FromRgb(0xFF, 0x9F, 0x0A), Reaction = "yawn" });
            _core.Mascot.React("yawn");
        }

        if (pct >= 25 || plugged)
        {
            _lowArmed = true;
        }

        _lastPercent = pct;
    }

    public void Dispose()
    {
        _poll.Stop();
        _trackTimer?.Stop();
        if (!_started)
        {
            return;
        }

        _core.Audio.VolumeChanged -= OnVolume;
        _core.Audio.MicChanged -= OnMic;
        _core.Playback.TrackChanged -= OnTrack;
        _core.Timer.Finished -= OnTimerFinished;
        _core.Session.Unlocked -= OnUnlock;
        _core.Session.Locking -= OnLock;
        SystemEvents.PowerModeChanged -= OnPower;
    }
}
