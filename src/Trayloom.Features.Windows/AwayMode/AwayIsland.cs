// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Brushes and small builders shared by the two Away mode island strips.</summary>
internal static class AwayIslandLook
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The camera glyph.</summary>
    public const string CameraGlyph = "";

    public static readonly Brush Amber = Frozen(Color.FromRgb(0xFF, 0xB3, 0x40));
    public static readonly Brush Grey = Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
    public static readonly Brush White = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));

    public static TextBlock Icon(Brush brush) => new()
    {
        Text = CameraGlyph,
        FontFamily = IconFont,
        FontSize = 12,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 6, 0),
    };

    public static TextBlock Text(string text, Brush brush, double maxWidth) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.Medium,
        Foreground = brush,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = maxWidth,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The "away.notice" strip: when the camera stops seeing the person, the island says so for a few seconds ("Closing your workspace in
/// 10 s. Move to cancel."). The island takes the "*.notice" strips on every <c>Invalidate()</c>, so the controller only has to queue the text.
/// </summary>
public sealed class AwayNoticeStrip : IIslandStrip
{
    public string? FeatureId => "away-mode";

    public int Order => 44;

    public string Id => "away.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AwayController>() is not { } controller)
        {
            return null;
        }

        controller.IslandContext = context;
        if (controller.TakeNotice() is not { } text)
        {
            return null;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(AwayIslandLook.Icon(AwayIslandLook.Amber));
        panel.Children.Add(AwayIslandLook.Text(text, AwayIslandLook.White, 260));
        return panel;
    }
}

/// <summary>
/// The "away.live" strip on the closed island: the countdown while Away mode is about to start, or a small "Checking you're here" while
/// the camera looks. It ranks after the built-in activities, which is fine: media playing keeps the camera off anyway.
/// </summary>
public sealed class AwayLiveStrip : IIslandStrip
{
    public string? FeatureId => "away-mode";

    public int Order => 906;

    public string Id => "away.live";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<AwayController>() is not { } controller)
        {
            return null;
        }

        // The island asks for live strips on every refresh of the closed island, so the context is known before the first countdown.
        controller.IslandContext = context;
        if (controller.PresencePhase == AwayPresencePhase.Countdown)
        {
            return new AwayCountdownView(controller, context);
        }

        if (controller.PresenceLooking)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(AwayIslandLook.Icon(AwayIslandLook.Grey));
            panel.Children.Add(AwayIslandLook.Text(AwayStrings.StripLooking, AwayIslandLook.Grey, 200));
            return panel;
        }

        return null;
    }
}

/// <summary>The countdown strip: a camera glyph and "Closing in 7 s", refreshed four times a second until the countdown is over.</summary>
internal sealed class AwayCountdownView : StackPanel
{
    private readonly AwayController _controller;
    private readonly IIslandContext _context;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public AwayCountdownView(AwayController controller, IIslandContext context)
    {
        _controller = controller;
        _context = context;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _text = AwayIslandLook.Text(Describe(), AwayIslandLook.White, 200);
        Children.Add(AwayIslandLook.Icon(AwayIslandLook.Amber));
        Children.Add(_text);
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        };
    }

    private string Describe()
    {
        var seconds = _controller.CountdownSecondsLeft;
        return _controller.CountdownCloses ? AwayStrings.StripClosing(seconds) : AwayStrings.StripMinimizing(seconds);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_controller.PresencePhase != AwayPresencePhase.Countdown)
        {
            // Cancelled or over: ask the island to drop the strip.
            _timer.Stop();
            _context.Invalidate();
            return;
        }

        _text.Text = Describe();
    }
}
