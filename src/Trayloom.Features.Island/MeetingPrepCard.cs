// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>
/// The "Up next" card at the top of the island's Calendar page while a meeting heads-up is on: Join, a microphone check, a camera check
/// and "Not now" (spec 10d). Nothing here opens a device until the person clicks its check.
/// </summary>
internal sealed class MeetingPrepCard : Border
{
    private const double MeterWidth = 150;
    private readonly IslandCore _core;
    private readonly CalEvent _event;
    private readonly TextBlock _when;
    private readonly StackPanel _micPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _micLine;
    private readonly Border _meter;
    private readonly Border _level;
    private readonly HoverButton _unmute;
    private readonly DispatcherTimer _tick;

    public MeetingPrepCard(IslandCore core, CalEvent meeting)
    {
        _core = core;
        _event = meeting;
        Background = D.White(0.07);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12, 10, 12, 10);
        Margin = new Thickness(0, 0, 0, 8);

        var column = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(D.T(Loc.T("Up next"), 10.5, FontWeights.SemiBold, D.Secondary));
        var title = D.T(meeting.Title.Length == 0 ? Loc.T("Untitled event") : meeting.Title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
        title.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(title);
        _when = D.Digits("", 11.5, FontWeights.Normal, D.Secondary);
        _when.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(_when);
        head.Children.Add(text);

        if (meeting.JoinUrl is { Length: > 0 })
        {
            var join = D.PillButton(Loc.T("Join"), () => core.Meeting.Join(), true);
            join.Margin = new Thickness(10, 0, 0, 0);
            join.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(join, 1);
            System.Windows.Automation.AutomationProperties.SetAutomationId(join, "Meeting.Join");
            head.Children.Add(join);
        }

        column.Children.Add(head);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Small(Loc.T("Check microphone"), ToggleMic));
        if (CameraAction() is { } camera)
        {
            actions.Children.Add(Small(Loc.T("Check camera"), camera));
        }

        actions.Children.Add(Small(Loc.T("Not now"), () => core.Meeting.Dismiss()));
        column.Children.Add(actions);

        _micLine = D.T("", 11.5, FontWeights.Normal, D.Secondary, false, TextTrimming.CharacterEllipsis);
        _micLine.MaxWidth = 220;
        _micLine.VerticalAlignment = VerticalAlignment.Center;
        _level = new Border { Background = D.Mint, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _meter = new Border { Width = MeterWidth, Height = 4, Background = D.White(0.14), CornerRadius = new CornerRadius(2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = _level };
        _unmute = D.PillButton(Loc.T("Unmute"), Unmute, true, height: 24);
        _unmute.Margin = new Thickness(10, 0, 0, 0);
        _unmute.Visibility = Visibility.Collapsed;
        _micPanel.Children.Add(_micLine);
        _micPanel.Children.Add(_meter);
        _micPanel.Children.Add(_unmute);
        column.Children.Add(_micPanel);

        Child = column;
        _tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        _tick.Tick += (_, _) => UpdateWhen();
        UpdateWhen();
        UpdateMic();
        Loaded += (_, _) =>
        {
            core.Meeting.Mic.Changed += UpdateMic;
            _tick.Start();
            UpdateWhen();
            UpdateMic();
        };
        Unloaded += (_, _) =>
        {
            core.Meeting.Mic.Changed -= UpdateMic;
            _tick.Stop();
        };
    }

    private static HoverButton Small(string text, Action click)
    {
        var b = D.PillButton(text, click, false, height: 24);
        b.Margin = new Thickness(0, 0, 6, 4);
        return b;
    }

    /// <summary>The camera check: the island's mirror page when it is there, else the Camera preview tool, else nothing.</summary>
    private Action? CameraAction()
    {
        if (_core.ModuleVisible("camera"))
        {
            return () =>
            {
                // The mirror page only starts the camera from its own "Open camera" button; the note makes that one start a 30 s look.
                _core.Meeting.NoteCameraCheck();
                _core.Open("camera");
            };
        }

        if (_core.Ctx.Runtime.IsAvailable("camera-preview"))
        {
            return () => _core.InvokeFeatureAction("camera-preview", "show", "open", "preview");
        }

        return null;
    }

    private void UpdateWhen()
    {
        var now = DateTime.Now;
        var range = $"{_event.Start.ToString("t", CultureInfo.CurrentCulture)} – {_event.End.ToString("t", CultureInfo.CurrentCulture)}";
        string tail;
        if (now >= _event.Start)
        {
            tail = Loc.T("Now").ToLower(CultureInfo.CurrentCulture);
        }
        else
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((_event.Start - now).TotalMinutes));
            tail = Loc.F("in {0}", Loc.F("{0} min", minutes));
        }

        _when.Text = range + " · " + tail;
    }

    private void ToggleMic()
    {
        var mic = _core.Meeting.Mic;
        if (mic.Running)
        {
            mic.Stop();
        }
        else
        {
            mic.Start(TimeSpan.FromSeconds(15));
        }
    }

    private void Unmute()
    {
        // An explicit click only: the check itself never touches the mute switch.
        _core.Audio.SetMicMuted(false);
    }

    private void UpdateMic()
    {
        var mic = _core.Meeting.Mic;
        if (!mic.Running && mic.Error is null)
        {
            _micPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _micPanel.Visibility = Visibility.Visible;
        if (mic.Error is { } error)
        {
            _micLine.Text = error;
            _micLine.Foreground = D.Orange;
            _micLine.MaxWidth = 360;
            _meter.Visibility = Visibility.Collapsed;
            _unmute.Visibility = Visibility.Collapsed;
            return;
        }

        _micLine.Foreground = D.Secondary;
        _micLine.MaxWidth = 220;
        _meter.Visibility = Visibility.Visible;
        _unmute.Visibility = mic.Muted ? Visibility.Visible : Visibility.Collapsed;
        _micLine.Text = mic.Muted ? Loc.T("Muted") : Loc.F("Microphone: {0}", mic.DeviceName ?? "") + (mic.Peak < 0.02 ? " · " + Loc.T("Speak to test") : "");
        // A square root makes a normal speaking voice move the bar well clear of the left edge.
        _level.Width = mic.Muted ? 0 : MeterWidth * Math.Sqrt(Math.Clamp(mic.Peak, 0, 1));
    }
}
