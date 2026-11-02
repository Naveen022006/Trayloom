// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Shared state of all system-monitor features: a ref-counted sampler (1 s interval, running only while the flyout
/// is open or at least one metric is pinned to the always-on-top readout), the history used by the sparklines,
/// the "System monitor" flyout and the pinned readout. Every monitor feature acquires it in Start and releases it in Stop.
/// </summary>
internal static class MonitorHub
{
    private const int HistoryLength = 60;

    private sealed class Member
    {
        public required string Id { get; init; }

        public required MonitorMetric Metric { get; init; }

        public required IFeatureContext Context { get; init; }

        public required Action<string> Handler { get; init; }
    }

    private static readonly List<Member> Members = [];
    private static readonly object SamplerLock = new();
    private static readonly Dictionary<string, List<double>> History = new()
    {
        ["cpu"] = [],
        ["gpu"] = [],
        ["mem"] = [],
        ["down"] = [],
        ["up"] = [],
        ["disk"] = [],
    };

    private static Dispatcher? _ui;
    private static SystemSampler? _sampler;
    private static Timer? _timer;
    private static int _busy;
    private static volatile int _generation;
    private static MonitorMetric _sampling;
    private static MonitorFlyout? _flyout;
    private static Osd? _readout;
    private static SystemSnapshot? _last;

    /// <summary>True when the feature is the first installed monitor feature (it contributes the shared flyout action).</summary>
    public static bool IsOwner(string id) => Members.Count > 0 && Members[0].Id == id;

    public static void Acquire(string id, MonitorMetric metric, IFeatureContext context)
    {
        Release(id);
        _ui = context.Ui;
        void Handler(string key)
        {
            if (key == "pinned")
            {
                context.Ui.BeginInvoke(Reevaluate);
            }
        }

        context.Settings.Changed += Handler;
        Members.Add(new Member { Id = id, Metric = metric, Context = context, Handler = Handler });
        CloseFlyout();
        Reevaluate();
    }

    public static void Release(string id)
    {
        var member = Members.FirstOrDefault(m => m.Id == id);
        if (member is null)
        {
            return;
        }

        member.Context.Settings.Changed -= member.Handler;
        Members.Remove(member);
        CloseFlyout();
        Reevaluate();
        if (Members.Count == 0)
        {
            _readout?.Close();
            _readout = null;
            _ui = null;
        }
    }

    public static void ToggleFlyout()
    {
        if (_ui is null || Members.Count == 0)
        {
            return;
        }

        if (_flyout is null)
        {
            _flyout = new MonitorFlyout(AllMetrics(), Reevaluate, Reevaluate);
        }

        _flyout.Toggle();
        if (_flyout.IsVisible && _last is not null)
        {
            _flyout.Update(_last, History);
        }
    }

    private static MonitorMetric AllMetrics() => Members.Aggregate(MonitorMetric.None, (acc, m) => acc | m.Metric);

    private static MonitorMetric PinnedMetrics() =>
        Members.Where(m => m.Context.Settings.Get("pinned", false)).Aggregate(MonitorMetric.None, (acc, m) => acc | m.Metric);

    private static void CloseFlyout()
    {
        if (_flyout is not null)
        {
            _flyout.Hide();
            _flyout.Close();
            _flyout = null;
        }
    }

    /// <summary>Starts or stops the sampler depending on whether anything is visible.</summary>
    private static void Reevaluate()
    {
        var wanted = PinnedMetrics();
        if (_flyout is { IsVisible: true })
        {
            wanted |= AllMetrics();
        }

        if (wanted == MonitorMetric.None)
        {
            StopSampler();
            _readout?.Dismiss();
            return;
        }

        _sampling = wanted;
        if (_timer is null)
        {
            _generation++;
            var generation = _generation;
            lock (SamplerLock)
            {
                _sampler ??= new SystemSampler();
            }

            _timer = new Timer(_ => Tick(generation), null, 0, 1000);
        }
        else
        {
            lock (SamplerLock)
            {
                _sampler?.Reset();
            }
        }
    }

    private static void StopSampler()
    {
        var timer = _timer;
        _timer = null;
        _generation++;
        timer?.Dispose();
        lock (SamplerLock)
        {
            _sampler?.Dispose();
            _sampler = null;
        }

        foreach (var list in History.Values)
        {
            list.Clear();
        }

        _last = null;
    }

    private static void Tick(int generation)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            SystemSnapshot snapshot;
            lock (SamplerLock)
            {
                if (_sampler is null || generation != _generation)
                {
                    return;
                }

                snapshot = _sampler.Sample(_sampling);
            }

            _ui?.BeginInvoke(() =>
            {
                if (generation == _generation)
                {
                    Publish(snapshot);
                }
            });
        }
        catch (Exception ex)
        {
            Members.FirstOrDefault()?.Context.Log.Error("System sampler failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static void Publish(SystemSnapshot s)
    {
        _last = s;
        Push("cpu", s.Cpu);
        Push("gpu", s.Gpu ?? 0);
        Push("mem", s.MemPercent);
        Push("down", s.NetDown);
        Push("up", s.NetUp);
        Push("disk", s.DiskActivity);

        if (_flyout is { IsVisible: true })
        {
            _flyout.Update(s, History);
        }

        UpdateReadout(s);
    }

    private static void Push(string key, double value)
    {
        var list = History[key];
        list.Add(value);
        if (list.Count > HistoryLength)
        {
            list.RemoveAt(0);
        }
    }

    private static void UpdateReadout(SystemSnapshot s)
    {
        var pinned = Members.Where(m => m.Context.Settings.Get("pinned", false)).Select(m => m.Metric).ToList();
        if (pinned.Count == 0)
        {
            _readout?.Dismiss();
            return;
        }

        var parts = new List<string>();
        foreach (var metric in pinned)
        {
            switch (metric)
            {
                case MonitorMetric.Cpu:
                    parts.Add($"CPU {MonitorFormat.Percent(s.Cpu)}");
                    break;
                case MonitorMetric.Gpu when s.GpuAvailable:
                    parts.Add($"GPU {MonitorFormat.Percent(s.Gpu ?? 0)}");
                    break;
                case MonitorMetric.Memory:
                    parts.Add($"RAM {MonitorFormat.Percent(s.MemPercent)}");
                    break;
                case MonitorMetric.Network:
                    parts.Add($"↓{MonitorFormat.Rate(s.NetDown)} ↑{MonitorFormat.Rate(s.NetUp)}");
                    break;
                case MonitorMetric.Disk:
                    parts.Add($"Disk {MonitorFormat.Percent(s.DiskActivity)}");
                    break;
                case MonitorMetric.Power when s.Power is { HasBattery: true } p:
                    parts.Add($"Battery {p.Percent}%{(p.OnAc ? "+" : string.Empty)}");
                    break;
            }
        }

        if (parts.Count == 0)
        {
            _readout?.Dismiss();
            return;
        }

        _readout ??= new Osd(corner: true);
        _readout.Display("", string.Join("   ", parts), 0);
    }
}
