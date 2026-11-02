// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WinKnife.Features.Capture;

internal sealed partial class RecorderEditorWindow
{
    private readonly Grid _stageGrid = new();
    private RecorderComposer? _composer;
    private RecorderPlan? _plan;
    private bool _rebuildDirty;
    private DispatcherTimer? _rebuildTimer;

    private DispatcherTimer _rebuild => _rebuildTimer ??= CreateRebuildTimer();

    private DispatcherTimer CreateRebuildTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            RebuildPreview();
        };
        return t;
    }

    /// <summary>Picture changes redraw after a beat (120 ms) so dragging a slider does not thrash the composer.</summary>
    private void ScheduleRebuild()
    {
        _rebuildDirty = true;
        _rebuild.Stop();
        _rebuild.Start();
    }

    /// <summary>Rebuilds the plan (pointer path, zoom spring, overlays) and the composer the stage shows.</summary>
    private void RebuildPreview()
    {
        _rebuild.Stop();
        if (_aiming || _pickingBlur)
        {
            _rebuildDirty = true;
            return;
        }

        _rebuildDirty = false;
        var plan = RecorderPlan.Build(_doc, Take.Pointer, Take.TypingTimes, new Size(Take.Width, Take.Height), Take.Fps, Duration);
        if (plan is null)
        {
            return;
        }

        if (_composer is not null)
        {
            _stageGrid.Children.Remove(_composer.Root);
            _composer.DetachSource();
        }

        var sizeChanged = _plan is null || _plan.Canvas != plan.Canvas;
        _plan = plan;
        if (sizeChanged)
        {
            BuildTransport();
        }

        _composer = new RecorderComposer(plan, _media, true);
        _composer.SetSelectedBlur(_selKind == SelKind.Blur ? _selId : null);
        _stageGrid.Children.Insert(0, _composer.Root);
        UpdateStage();
    }

    /// <summary>Draws the frame for the playhead: the plan index of the edited-clock time.</summary>
    private void UpdateStage()
    {
        if (_composer is null || _plan is null)
        {
            return;
        }

        var outTime = _doc.OutputTime(_src, Duration) ?? 0;
        _composer.Apply(_plan.FrameIndex(outTime), outTime);
    }
}
