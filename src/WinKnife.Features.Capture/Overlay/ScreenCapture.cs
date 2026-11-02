// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Entry point of the unified capture chooser: delay, overlay, and hand-over of the outcome to the right tool.</summary>
internal static class ScreenCapture
{
    private static DispatcherTimer? _countdown;
    private static int _generation;

    public static bool IsCountingDown => _countdown is not null;

    /// <summary>Opens the chooser on <paramref name="tool"/>. A shortcut start honours "Show capture menu…" for that tool.</summary>
    public static void Start(CaptureTool tool, bool fromShortcut = false, bool scrollingOnly = false)
    {
        if (!CaptureHost.IsAttached)
        {
            return;
        }

        var s = CaptureHost.Services;
        if (CancelCountdown())
        {
            return;
        }

        if (CaptureSession.IsActive || CaptureHost.SessionOnScreen)
        {
            return;
        }

        var showMenu = !fromShortcut || s.App.Get<bool>(CaptureTools.ShowMenuKey(tool));
        var recording = CaptureHost.Recorder?.IsRecording == true;
        if (recording && tool != CaptureTool.Recording && showMenu)
        {
            return; // the chooser's own tool strip offers only its tool during a recording
        }

        if (tool == CaptureTool.Recording && !CaptureHost.Recorder!.CanPrepare())
        {
            return;
        }

        var delay = tool == CaptureTool.Screenshot && !scrollingOnly ? Math.Max(0, s.App.Get<int>(K.Delay)) : 0;
        void Open() => OpenOverlay(tool, showMenu, scrollingOnly);
        if (delay > 0)
        {
            RunCountdown(delay, Open);
        }
        else
        {
            Open();
        }
    }

    private static void OpenOverlay(CaptureTool tool, bool showMenu, bool scrollingOnly)
    {
        var request = new CaptureRequest { Tool = tool, ShowMenu = showMenu, ScrollingOnly = scrollingOnly, Done = Dispatch };
        CaptureSession.Begin(request);
    }

    /// <summary>Countdown HUD (3...2...1) before an action; returns false when delay is zero (caller runs the action itself).</summary>
    public static void RunCountdown(int seconds, Action then)
    {
        CancelCountdown();
        var gen = ++_generation;
        var remaining = seconds;
        CaptureHost.SessionOnScreen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown = timer;
        QuickToolHud.ShowCountdown(remaining);
        timer.Tick += (_, _) =>
        {
            if (gen != _generation)
            {
                timer.Stop();
                return;
            }

            remaining--;
            if (remaining <= 0)
            {
                timer.Stop();
                _countdown = null;
                CaptureHost.SessionOnScreen = false;
                QuickToolHud.Dismiss();
                // give the HUD one frame to leave before pixels are taken
                CaptureHost.Ui.BeginInvoke(DispatcherPriority.Background, then);
                return;
            }

            QuickToolHud.ShowCountdown(remaining);
        };
        timer.Start();
    }

    public static bool CancelCountdown()
    {
        if (_countdown is null)
        {
            return false;
        }

        _generation++;
        _countdown.Stop();
        _countdown = null;
        CaptureHost.SessionOnScreen = false;
        QuickToolHud.Dismiss();
        return true;
    }

    private static void Dispatch(CaptureOutcome outcome)
    {
        switch (outcome)
        {
            case PixelsOutcome { Tool: CaptureTool.Screenshot } p:
                CaptureHost.Screenshot?.Route(p.Capture);
                break;
            case PixelsOutcome { Tool: CaptureTool.Text } p:
                CaptureHost.Text?.Recognize(p.Capture.Image, detectQr: CaptureHost.App.Get<bool>(K.OcrQr));
                break;
            case ColorOutcome c:
                CaptureHost.ColorSampler?.Received(c);
                break;
            case GeometryOutcome { Tool: CaptureTool.Recording } g:
                CaptureHost.Recorder?.OnRegion(g.Pick);
                break;
            case GeometryOutcome { Scrolling: true } g:
                CaptureHost.Screenshot?.StartScrolling(g.Pick);
                break;
        }
    }

    public static void StopAll()
    {
        CancelCountdown();
        CaptureSession.CancelCurrent();
    }
}
