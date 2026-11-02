// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>Scroll over the island to open or close it; swipe sideways (or Shift + wheel) to change track.</summary>
public sealed class IslandGesturesFeature : IFeature
{
    private const uint SequenceGapMs = 350;
    private const uint CooldownMs = 450;

    private IFeatureContext? _context;
    private IDisposable? _subscription;

    // Hook-thread state (only touched from the hook thread).
    private int _accumulated;
    private uint _lastEvent;
    private uint _lockedUntil;
    private bool _locked;
    private volatile bool _invert;
    private volatile bool _tracks = true;

    public string Id => "island-gestures";

    public string Name => "Island gestures";

    public string Description => "Scroll down over the island to open it, scroll up to close it, and swipe sideways to skip tracks.";

    public FeatureGroup Group => FeatureGroup.Island;

    public void Start(IFeatureContext context)
    {
        _context = context;
        _accumulated = 0;
        _locked = false;
        ReadSettings();
        context.Settings.Changed += OnSettingChanged;
        _subscription = context.Hooks.SubscribeMouse(OnMouse, priority: 10);
    }

    private void ReadSettings()
    {
        if (_context is null)
        {
            return;
        }

        _invert = _context.Settings.Get("invert", false);
        _tracks = _context.Settings.Get("tracks", true);
    }

    private void OnSettingChanged(string key) => ReadSettings();

    public void Stop()
    {
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        _subscription?.Dispose();
        _subscription = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddToggle("invert", "Reverse the scroll direction", false, "Scroll up to open and down to close.")
            .AddToggle("tracks", "Change track with a sideways swipe or Shift + wheel", true)
            .Build();
    }

    /// <summary>Runs on the hook thread: a rectangle test and a couple of integer operations, then hand off to the UI.</summary>
    private bool OnMouse(MouseHookEvent e)
    {
        if (!e.IsWheel || e.IsFromWinKnife || _context is null)
        {
            return false;
        }

        var pill = IslandHub.Pill;
        if (pill is null || e.X < pill.Left || e.X >= pill.Right || e.Y < pill.Top || e.Y >= pill.Bottom)
        {
            return false;
        }

        var context = _context;
        var horizontal = e.Kind == MouseEventKind.HorizontalWheel || (e.Modifiers & ModifierKeys.Shift) != 0;
        if (horizontal)
        {
            if (!_tracks)
            {
                return false;
            }
        }
        else
        {
            // Over a scrolling list the wheel belongs to the list, except in the header strip.
            if (pill.Expanded && pill.ContentScrolls && e.Y >= pill.HeaderBottom)
            {
                return false;
            }
        }

        var timestamp = e.Timestamp;
        if (_locked && unchecked(timestamp - _lockedUntil) > int.MaxValue)
        {
            return true; // still cooling down after a gesture: swallow so nothing underneath scrolls
        }

        _locked = false;
        if (timestamp - _lastEvent > SequenceGapMs)
        {
            _accumulated = 0;
        }

        _lastEvent = timestamp;
        var delta = horizontal && e.Kind == MouseEventKind.Wheel ? -e.WheelDelta : e.WheelDelta;
        if (_accumulated != 0 && Math.Sign(_accumulated) != Math.Sign(delta))
        {
            _accumulated = 0;
        }

        _accumulated += delta;
        if (Math.Abs(_accumulated) < 120)
        {
            return true;
        }

        var direction = Math.Sign(_accumulated); // +1 = wheel up / swipe right
        _accumulated = 0;
        _locked = true;
        _lockedUntil = timestamp + CooldownMs;

        var invert = _invert;
        var expanded = pill.Expanded;
        context.Ui.BeginInvoke(() =>
        {
            var island = IslandHub.Controller;
            if (island is null)
            {
                return;
            }

            if (horizontal)
            {
                if (direction > 0)
                {
                    island.NextTrack();
                }
                else
                {
                    island.PreviousTrack();
                }

                return;
            }

            var open = invert ? direction > 0 : direction < 0;
            if (open && !expanded)
            {
                island.Expand();
            }
            else if (!open && expanded)
            {
                island.Collapse();
            }
        });
        return true;
    }
}
