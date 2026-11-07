// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

/// <summary>
/// Watches for a new local dev server and queues the "{0} is up" island notice. It reads the port list every 10 s, and only while the
/// island is running and enabled, so it costs nothing when nobody could see the notice.
/// </summary>
internal sealed class DevServerWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IContributionServices _services;
    private readonly HashSet<int> _known = [];
    private DispatcherTimer? _timer;
    private bool _seeded;
    private bool _reading;
    private DateTime _lastNotice = DateTime.MinValue;

    public DevServerWatcher(IContributionServices services)
    {
        _services = services;
    }

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _seeded = false;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _services.Ui) { Interval = Interval };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _known.Clear();
        _seeded = false;
        DevServerNotices.Clear();
    }

    private async void Tick()
    {
        if (_reading)
        {
            return;
        }

        if (!_services.Runtime.IsRunning("notch") || !_services.App.Get<bool>("notchEnabled"))
        {
            // With the island off nobody sees a notice; whatever runs when it comes back on is the new baseline, not news.
            _seeded = false;
            return;
        }

        _reading = true;
        try
        {
            var showAll = _services.App.Get<bool>("devServersShowAll");
            var list = await Task.Run(() =>
            {
                var all = PortList.Read();
                return all is null ? null : DevServerRules.Filter(all, Sessions.IsMine, showAll);
            }).ConfigureAwait(true);
            if (list is null || _timer is null)
            {
                return;
            }

            var fresh = _seeded ? DevServerRules.NewPorts(_known, list) : [];
            _known.Clear();
            foreach (var entry in list)
            {
                _known.Add(entry.Port);
            }

            _seeded = true;
            if (fresh.Count > 0 && DateTime.UtcNow - _lastNotice >= Interval)
            {
                _lastNotice = DateTime.UtcNow;
                DevServerNotices.Add(Loc.F("{0} is up", "localhost:" + fresh[0]));
                _services.Log.Info($"Dev servers: {fresh.Count} new");
                DevServerNotices.Context?.Invalidate();
            }
        }
        catch (Exception ex)
        {
            _services.Log.Error("Dev servers: watcher failed", ex);
        }
        finally
        {
            _reading = false;
        }
    }
}
