// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>
/// Owns the tracker and the store, and is what the island page, the resting strip and the settings card read (published through
/// the service hub by <see cref="ScreenTimeFeature"/>). Everything here runs on the UI thread.
/// </summary>
internal sealed class ScreenTimeRuntime
{
    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly Action<string> _onSetting;
    private double? _yesterday;
    private DateTime _yesterdayFor = DateTime.MinValue;
    private bool _stopped;

    public ScreenTimeRuntime(IAppSettings app, ILog log, Dispatcher ui, ScreenTimeStore store)
    {
        _app = app;
        _log = log;
        _ui = ui;
        Store = store;
        Tracker = new ScreenTimeTracker(app, log, ui, store);
        Tracker.Changed += () => Changed?.Invoke();
        Tracker.DayChanged += RefreshYesterday;
        _onSetting = key => _ui.BeginInvoke(new Action(() => OnSetting(key)));
    }

    public ScreenTimeStore Store { get; }

    public ScreenTimeTracker Tracker { get; }

    /// <summary>Raised (UI thread) when the numbers shown may have changed, at most every 10 s.</summary>
    public event Action? Changed;

    /// <summary>The day being counted.</summary>
    public DayUsage Today => Tracker.Today;

    /// <summary>True when counting is switched on in the settings.</summary>
    public bool IsEnabled => _app.Get(ScreenTimeKeys.Enabled, true);

    /// <summary>Seconds of yesterday, or null when there is no file for it (read from disk once per day, not on every draw).</summary>
    public double? YesterdayTotal
    {
        get
        {
            if (_yesterdayFor != Today.Date)
            {
                RefreshYesterday();
            }

            return _yesterday;
        }
    }

    /// <summary>Starts counting when enabled and listens for the settings that switch it.</summary>
    public void Start()
    {
        _stopped = false;
        Store.Sweep(Math.Max(1, _app.Get(ScreenTimeKeys.KeepDays, 7)), DateTime.Now);
        RefreshYesterday();
        Tracker.Attach();
        _app.Changed += _onSetting;
        Apply();
    }

    /// <summary>Credits up to now, flushes, unhooks and stops listening.</summary>
    public void Stop()
    {
        _stopped = true;
        _app.Changed -= _onSetting;
        Tracker.Detach();
    }

    /// <summary>"Clear history…": deletes every day file and resets today in memory.</summary>
    public void Clear()
    {
        Store.Clear();
        Tracker.ResetToday();
        _yesterday = null;
        _yesterdayFor = Today.Date;
        _log.Info("[screen-time] history cleared");
        Changed?.Invoke();
    }

    /// <summary>The compact, full-sentence line of the three most used apps of today for the settings card.</summary>
    public string TopApps(int count) =>
        string.Join(", ", Today.Ranked().Take(count).Select(a => a.Name + " " + ScreenTimeFormat.Long(a.Seconds)));

    /// <summary>Writes the in-memory day plus the tracker state to <c>dump.json</c> and logs one line.</summary>
    public void Dump()
    {
        var reason = Tracker.ActiveReason();
        var shape = new DayFile
        {
            Tracking = Tracker.Tracking,
            Active = reason is "input" or "media" or "fullscreen",
            ActiveReason = reason,
        };
        var temp = Path.Combine(Store.Root, "dump.json");
        try
        {
            Directory.CreateDirectory(Store.Root);
            var day = Today;
            var file = shape;
            file.Date = day.Date.ToString("yyyy-MM-dd");
            file.First = day.First;
            file.Total = (int)Math.Round(day.Total);
            for (var h = 0; h < 24; h++)
            {
                file.Hours[h] = (int)Math.Round(day.Hours[h]);
            }

            file.Apps = [.. day.Ranked().Select(a => new AppFile { Id = a.Id, Name = a.Name, Seconds = (int)Math.Round(a.Seconds) })];
            File.WriteAllText(temp + ".tmp", System.Text.Json.JsonSerializer.Serialize(file));
            File.Move(temp + ".tmp", temp, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"[screen-time] cannot write dump: {ex.HResult}");
        }

        _log.Info($"[screen-time] dump: total={(int)Math.Round(Today.Total)} s, apps={Today.Apps.Count}, active={reason}");
    }

    private void OnSetting(string key)
    {
        if (_stopped)
        {
            return;
        }

        switch (key)
        {
            case ScreenTimeKeys.Enabled:
                Apply();
                Changed?.Invoke();
                break;
            case ScreenTimeKeys.KeepDays:
                Store.Sweep(Math.Max(1, _app.Get(ScreenTimeKeys.KeepDays, 7)), DateTime.Now);
                break;
        }
    }

    private void Apply()
    {
        if (IsEnabled)
        {
            Tracker.StartTracking();
        }
        else
        {
            Tracker.StopTracking();
        }
    }

    private void RefreshYesterday()
    {
        var date = Today.Date.AddDays(-1);
        _yesterdayFor = Today.Date;
        _yesterday = Store.Load(date)?.Total;
    }
}
