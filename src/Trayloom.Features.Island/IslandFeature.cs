// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>The Dynamic Island (catalog id <c>notch</c>): a pill at the top of the screen that expands into pages (spec 03 Parts B-D).</summary>
public sealed class IslandFeature : IFeature
{
    private IFeatureContext? _context;
    private IslandCore? _core;
    private IslandWindow? _window;
    private IDisposable? _shortcut;
    private IDisposable? _wheel;
    private bool _stopped = true;
    private readonly GestureState _gesture = new();

    public string Id => "island";

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_core is null || _window is null)
            {
                return [];
            }

            var core = _core;
            // Developer aid (TRAYLOOM_DEV=1): one action per page, so screenshots can open every page without synthesized input.
            var dev = Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1"
                ? IslandCatalog.Modules.Select(m => new FeatureAction("dev-open-" + m.Id, "Island page: " + m.Title, () => _window?.Open(m.Id), G.Controls)).ToList()
                : [];
            return
            [
                .. dev,
                new FeatureAction("toggle", Loc.T("Open or close Dynamic Island"), () => _window?.Toggle(), G.Controls),
                new FeatureAction("explore", Loc.T("Explore the island sections"), () => _window?.Open("explore"), G.Explore),
                new FeatureAction("open-music", Loc.T("Open the island music page"), () => _window?.Open("music"), G.Music),
                new FeatureAction("play-pause", Loc.T("Play or pause music"), () => Transport(core, AmAction.PlayPause, core.Playback.TogglePlayPause), G.Play),
                new FeatureAction("next", Loc.T("Next track"), () => Transport(core, AmAction.SkipForward, core.Playback.Next), G.Next),
                new FeatureAction("previous", Loc.T("Previous track"), () => Transport(core, AmAction.SkipBack, core.Playback.Previous), G.Previous),
                new FeatureAction("timer", Loc.T("Open the island timer"), () => _window?.Open("timer"), G.Timer),
                new FeatureAction("calendar", Loc.T("Open the island calendar"), () => _window?.Open("calendar"), G.Calendar),
            ];
        }
    }

    /// <summary>
    /// A transport shortcut: with nothing playing it does what the island's idle buttons do (Apple Music is started in the background and
    /// played, or the preferred player is opened), because there is no media session to command; otherwise it commands the current one.
    /// </summary>
    private static void Transport(IslandCore core, AmAction action, Action current)
    {
        if (!core.Playback.Current.HasTrack)
        {
            var preferred = PlaybackService.ResolvePreferredPlayer(core.App.Get<string>(K.PreferredPlayer), core.Playback);
            if (preferred is not null || MusicUi.HiddenApple(core, preferred))
            {
                MusicUi.IdleAction(core, preferred, action)();
                return;
            }
        }

        current();
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        _stopped = false;
        K.RegisterDefaults(context.App);
        BrightnessService.Log = (message, exception) =>
        {
            if (exception is null)
            {
                context.Log.Info(message);
            }
            else
            {
                context.Log.Warn(message);
            }
        };
        BrightnessService.Probe();
        _core = new IslandCore(context);
        _core.Start();
        context.App.Changed += OnSetting;
        context.Runtime.Changed += OnRuntime;
        _shortcut = context.Shortcuts.Bind("notch", () =>
        {
            if (_window is not null)
            {
                _window.Toggle();
            }
        });
        _wheel = context.Hooks.SubscribeMouse(OnMouse, 10);
        SyncEnabled();
    }

    public void Stop()
    {
        _stopped = true;
        if (_context is not null)
        {
            _context.App.Changed -= OnSetting;
            _context.Runtime.Changed -= OnRuntime;
        }

        _shortcut?.Dispose();
        _wheel?.Dispose();
        _shortcut = _wheel = null;
        IslandRects.Pill = null;
        BrightnessService.Log = null;
        _window?.Shutdown();
        _window = null;
        _core?.Dispose();
        _core = null;
        _context = null;
    }

    private void OnRuntime() => _context?.Ui.BeginInvoke(SyncEnabled);

    private void OnSetting(string key)
    {
        if (key == K.Enabled)
        {
            _context?.Ui.BeginInvoke(SyncEnabled);
        }
    }

    private void SyncEnabled()
    {
        if (_stopped || _context is null || _core is null)
        {
            return;
        }

        var on = _context.App.Get<bool>(K.Enabled);
        if (on && _window is null)
        {
            _window = new IslandWindow(_core);
            _window.Begin();
        }
        else if (!on && _window is not null)
        {
            _window.Shutdown();
            _window = null;
        }
    }

    // ---------------------------------------------------------------- gestures (hook thread)

    private sealed class GestureState
    {
        public double AccV;
        public double AccH;
        public uint Last;
        public bool Decided;
        public uint LockedUntil;

        /// <summary>The decided gesture owns the rest of its physical scroll sequence (momentum must not scroll the page it just opened).</summary>
        public bool Swallow;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (!e.IsWheel || e.IsFromTrayloom || _context is null)
        {
            return false;
        }

        var pill = IslandRects.Pill;
        if (pill is null || !_context.App.Get<bool>(K.Gestures) || !_context.Runtime.IsAvailable("notch-gestures"))
        {
            return false;
        }

        var inside = e.X >= pill.Left && e.X < pill.Right && e.Y >= pill.Top && e.Y < pill.Bottom;
        if (!inside)
        {
            return false;
        }

        // over a slider of the open page (brightness, volume) the wheel belongs to the slider, not to the collapse gesture
        if (pill.Expanded && pill.WheelZones.Any(z => e.X >= z.L && e.X < z.R && e.Y >= z.T && e.Y < z.B))
        {
            var s = _gesture;
            if (s.Decided && s.Swallow && unchecked(e.Timestamp - s.Last) <= 350)
            {
                // the scroll sequence that just opened the island (or changed the song) is still running: its remaining notches and the
                // touchpad's momentum must not move the slider that has appeared under the pointer
                s.Last = e.Timestamp;
                return true;
            }

            s.AccV = s.AccH = 0;
            s.Decided = false;
            s.Swallow = false;
            return false;
        }

        if ((e.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return false;
        }

        var g = _gesture;
        var t = e.Timestamp;
        if (t - g.Last > 350)
        {
            g.AccV = g.AccH = 0;
            g.Decided = false;
            g.Swallow = false;
        }

        g.Last = t;
        var horizontal = e.Kind == MouseEventKind.HorizontalWheel || (e.Modifiers & ModifierKeys.Shift) != 0;
        var points = e.WheelDelta / 120.0 * 24.0;
        if (horizontal)
        {
            g.AccH += e.Kind == MouseEventKind.Wheel ? -points : points;
        }
        else
        {
            g.AccV += points;
        }

        if (g.Decided && unchecked(t - g.LockedUntil) < int.MaxValue)
        {
            return g.Swallow;
        }

        var absV = Math.Abs(g.AccV);
        var absH = Math.Abs(g.AccH);
        if (absV + absH < 4)
        {
            return false;
        }

        var ui = _context.Ui;
        var window = _window;
        if (window is null)
        {
            return false;
        }

        if (absH > absV * 1.5 && absH >= 40)
        {
            g.Decided = true;
            g.LockedUntil = t + 500;
            g.Swallow = pill.MusicShowing;
            if (!pill.MusicShowing)
            {
                return false;
            }

            var next = g.AccH < 0;
            ui.BeginInvoke(() =>
            {
                if (next)
                {
                    _core?.Playback.Next();
                }
                else
                {
                    _core?.Playback.Previous();
                }
            });
            return true;
        }

        if (absV > absH * 1.5 && absV >= 16)
        {
            var down = g.AccV < 0; // wheel toward the user
            if (!pill.Expanded)
            {
                g.Decided = true;
                g.LockedUntil = t + 450;
                g.Swallow = down;
                if (down)
                {
                    ui.BeginInvoke(() => window.Open(takeFocus: true));
                    return true;
                }

                return false;
            }

            // open island: scroll up over the top row (or a page without a scrolling list) closes it
            var inHeader = e.Y < pill.HeaderBottom;
            if (!down && (inHeader || !pill.ContentScrolls) && inHeader)
            {
                g.Decided = true;
                g.LockedUntil = t + 450;
                g.Swallow = true;
                ui.BeginInvoke(() => window.Collapse());
                return true;
            }
        }

        return false;
    }
}
