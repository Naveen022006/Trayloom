// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Microsoft.Win32;

namespace Trayloom.Features.Maintenance;

/// <summary>
/// Runs one lightweight pass per day (09:00) while automatic WhatsApp cleanup is on (Vorssaint's
/// <c>WhatsAppDownloadScheduler</c>). Missed passes are recovered after launch, wake and clock changes, and nothing stays alive
/// when either the tool or the automation is off.
/// </summary>
internal sealed class WaScheduler
{
    private System.Threading.Timer? _timer;
    private bool _hooked;
    private bool _running;

    public static WaScheduler Shared { get; } = new();

    public event Action? Changed;

    public DateTime? NextFire { get; private set; }

    private static bool Armed =>
        Host.IsAttached && Host.Runtime.IsAvailable("cleaner")
        && Host.App.Get<bool>("whatsAppDownloadsEnabled")
        && Host.App.Get<bool>("whatsAppDownloadsAutomaticEnabled")
        && Host.App.Get<bool>("whatsAppDownloadsAccessConfirmed");

    public void Sync()
    {
        if (!Armed)
        {
            Stop();
            return;
        }

        var app = Host.App;
        if (app.Get<double>("whatsAppDownloadsAutomaticStartDate") <= 0 && !app.Get<bool>("whatsAppDownloadsIncludeExisting"))
        {
            app.Set("whatsAppDownloadsAutomaticStartDate", Fmt.Epoch());
        }

        Hook();
        ScheduleNext();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Unhook();
        NextFire = null;
        V.Post(() => Changed?.Invoke());
    }

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.TimeChanged += OnTime;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        _hooked = false;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.TimeChanged -= OnTime;
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            V.Post(ScheduleNext);
        }
    }

    private void OnTime(object? sender, EventArgs e) => V.Post(ScheduleNext);

    private void ScheduleNext()
    {
        if (!Armed)
        {
            return;
        }

        var now = DateTime.Now;
        var stamp = Host.App.Get<double>("whatsAppDownloadsLastAutoRun");
        DateTime? last = stamp > 0 ? Fmt.FromEpoch(stamp) : null;
        var fire = WaRules.MissedAutomaticCheck(now, last) ? now.AddSeconds(120) : WaRules.NextAutomaticCheck(now);
        ScheduleAt(fire);
    }

    private void ScheduleAt(DateTime date)
    {
        _timer?.Dispose();
        var due = date - DateTime.Now;
        if (due < TimeSpan.FromSeconds(1))
        {
            due = TimeSpan.FromSeconds(1);
        }

        if (due > TimeSpan.FromDays(2))
        {
            due = TimeSpan.FromDays(2);
        }

        _timer = new System.Threading.Timer(_ => V.Post(RunAutomaticCleanup), null, due, Timeout.InfiniteTimeSpan);
        var changed = NextFire != date;
        NextFire = date;
        if (changed)
        {
            V.Post(() => Changed?.Invoke());
        }
    }

    private void RunAutomaticCleanup()
    {
        var manager = WaManager.Shared;
        var organizer = WaOrganizer.Shared;
        var userIsReviewing = manager.Phase == WaPhase.Results && manager.ReviewVisible;
        if (_running || organizer.IsBusy || manager.Phase is WaPhase.Scanning or WaPhase.Cleaning || userIsReviewing)
        {
            ScheduleAt(DateTime.Now.AddSeconds(600));
            return;
        }

        _running = true;

        void OnChange()
        {
            switch (manager.Phase)
            {
                case WaPhase.Results:
                    // The person may have opened the review while the pass's scan was in flight; automation must never
                    // rewrite their checkboxes or clean under their eyes. Step aside and try again later.
                    if (manager.ReviewVisible)
                    {
                        manager.Changed -= OnChange;
                        _running = false;
                        ScheduleAt(DateTime.Now.AddSeconds(600));
                        return;
                    }

                    manager.SelectAutomaticRules();
                    if (manager.SelectedCount > 0)
                    {
                        manager.CleanSelected(true);
                    }
                    else
                    {
                        manager.CompleteAutomaticWithoutCleaning();
                    }

                    break;
                case WaPhase.Done:
                    manager.Changed -= OnChange;
                    FinishRun(manager.DoneMoved, manager.DoneBytes, manager.DoneFailed, true);
                    break;
                case WaPhase.Failed:
                    manager.Changed -= OnChange;
                    FinishRun(0, 0, 1, false);
                    break;
            }
        }

        manager.Changed += OnChange;
        manager.Scan();
    }

    private void FinishRun(int moved, long bytes, int failed, bool notify)
    {
        _running = false;
        Host.App.Set("whatsAppDownloadsLastAutoRun", Fmt.Epoch());
        if (notify && Host.App.Get<bool>("whatsAppDownloadsNotify"))
        {
            Host.Notify(WaText.NotificationTitle, WaText.NotificationFormat(moved, Fmt.Bytes(bytes), failed));
        }

        ScheduleNext();
    }
}
