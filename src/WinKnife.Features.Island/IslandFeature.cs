// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>A black pill at the top of the screen that shows now playing and expands into a panel of extensions.</summary>
public sealed class IslandFeature : IFeature
{
    private IFeatureContext? _context;
    private IslandWindow? _window;
    private MediaService? _media;
    private MusicSection? _music;
    private DispatcherTimer? _poll;
    private nint _foregroundHook;
    private IslandNative.WinEventProc? _foregroundProc;
    private bool _stopped = true;

    public string Id => "island";

    public string Name => "Dynamic Island";

    public string Description => "A pill at the top of your screen with now playing controls that expands into timers, a calendar and recent downloads.";

    public FeatureGroup Group => FeatureGroup.Island;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", "Expand or collapse the island", () => _window?.Toggle(), ""),
        new FeatureAction("play-pause", "Play or pause music", () => _media?.TogglePlayPause(), ""),
        new FeatureAction("next", "Next track", () => _media?.Next(), ""),
        new FeatureAction("previous", "Previous track", () => _media?.Previous(), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _stopped = false;
        _media = new MediaService(context.Ui, context.Log);
        _music = new MusicSection(_media, context.Ui);
        IslandHub.RegisterSection(_music.ToSection());

        _window = new IslandWindow(_media);
        ApplySettings();
        _window.Reposition(UseCursorMonitor);
        _window.ShowNoActivate();
        _window.Reposition(UseCursorMonitor);
        _media.Start();

        context.Settings.Changed += OnSettingChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        _foregroundProc = (_, _, _, _, _, _, _) => context.Ui.BeginInvoke(DispatcherPriority.Background, EvaluateVisibility);
        _foregroundHook = IslandNative.SetWinEventHook(
            IslandNative.EventSystemForeground,
            IslandNative.EventSystemForeground,
            0,
            _foregroundProc,
            0,
            0,
            IslandNative.WinEventOutOfContext | IslandNative.WinEventSkipOwnProcess);

        UpdatePoll();
        EvaluateVisibility();
    }

    public void Stop()
    {
        _stopped = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        if (_foregroundHook != 0)
        {
            IslandNative.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        _foregroundProc = null;
        _poll?.Stop();
        _poll = null;
        _window?.Shutdown();
        _window = null;
        _music?.Dispose();
        _music = null;
        IslandHub.UnregisterSection("music");
        _media?.Dispose();
        _media = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddChoice(
                "trigger",
                "Open the island",
                new Choice<string>[] { new("hover", "When the pointer rests on it"), new("click", "When I click it") },
                "hover")
            .AddChoice(
                "monitor",
                "Show on",
                new Choice<string>[] { new("primary", "Primary monitor"), new("cursor", "Monitor under the pointer") },
                "primary")
            .AddChoice(
                "collapseDelay",
                "Close after the pointer leaves",
                new Choice<int>[] { new(200, "Quickly"), new(450, "Short delay"), new(900, "Long delay"), new(2000, "Very long delay") },
                450)
            .AddIntSlider("topGap", "Distance from the top edge", 0, 24, 0)
            .AddToggle("compactTitle", "Show the track name in the collapsed island", true)
            .AddToggle("hideInFullscreen", "Hide while a full-screen app or game is in front", true)
            .Build();
    }

    private bool UseCursorMonitor => _context?.Settings.Get("monitor", "primary") == "cursor";

    private void ApplySettings()
    {
        if (_window is null || _context is null)
        {
            return;
        }

        var s = _context.Settings;
        _window.ExpandOnHover = s.Get("trigger", "hover") != "click";
        _window.CollapseDelayMs = s.Get("collapseDelay", 450);
        _window.TopGap = s.Get("topGap", 0);
        _window.ShowTitleInCompact = s.Get("compactTitle", true);
    }

    private void OnSettingChanged(string key)
    {
        _context?.Ui.BeginInvoke(() =>
        {
            if (_stopped || _window is null)
            {
                return;
            }

            ApplySettings();
            _window.Reposition(UseCursorMonitor);
            UpdatePoll();
            EvaluateVisibility();
        });
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        _context?.Ui.BeginInvoke(() =>
        {
            if (_stopped || _window is null)
            {
                return;
            }

            _window.Reposition(UseCursorMonitor);
            EvaluateVisibility();
        });
    }

    /// <summary>A slow timer is only needed to catch fullscreen toggles inside one window and to follow the pointer between monitors.</summary>
    private void UpdatePoll()
    {
        var needed = _context is not null && (UseCursorMonitor || _context.Settings.Get("hideInFullscreen", true));
        if (!needed)
        {
            _poll?.Stop();
            return;
        }

        if (_poll is null)
        {
            _poll = new DispatcherTimer(DispatcherPriority.Background, _context!.Ui) { Interval = TimeSpan.FromMilliseconds(1500) };
            _poll.Tick += (_, _) =>
            {
                if (_window is { IsExpanded: false } window && UseCursorMonitor)
                {
                    var cursorArea = Screens.UnderCursor();
                    if (cursorArea.Bounds != window.Area.Bounds)
                    {
                        window.Reposition(true);
                    }
                }

                EvaluateVisibility();
            };
        }

        _poll.Start();
    }

    private void EvaluateVisibility()
    {
        if (_stopped || _window is null || _context is null)
        {
            return;
        }

        var hide = false;
        if (_context.Settings.Get("hideInFullscreen", true))
        {
            var (monitor, bounds) = _window.MonitorInfo();
            try
            {
                hide = IslandNative.IsFullscreenForeground(monitor, bounds);
            }
            catch
            {
                hide = false;
            }
        }

        _window.SetSuppressed(hide);
    }
}
