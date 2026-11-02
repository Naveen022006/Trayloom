// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// The shared monitor hub (spec 05 F1). One sampler and one timer, running only while a monitor surface is visible (a panel tab or detail
/// view), a tray metric is enabled or an alert is on; otherwise nothing runs. Interval = <c>monitorIntervalSeconds</c> (1 / 2 / 5 s). The
/// hub keeps the last snapshot and 120-sample histories for the graphs. Every installed monitor feature acquires it in Start.
/// </summary>
internal static class MonitorService
{
    private const int HistoryLength = 120;

    private sealed class Lease(Needs needs, Action release) : IDisposable
    {
        public Needs Needs { get; } = needs;

        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                release();
            }
        }
    }

    private static readonly object Gate = new();
    private static readonly List<Lease> Leases = [];
    private static readonly Dictionary<string, List<double>> History = [];
    private static readonly List<IFeatureContext> Members = [];
    private static readonly Dictionary<string, Needs> Backgrounds = [];
    private static Dispatcher? _ui;
    private static IAppSettings? _app;
    private static IFeatureRuntime? _runtime;
    private static ILog? _log;
    private static SystemSampler? _sampler;
    private static Timer? _timer;
    private static int _busy;
    private static int _generation;
    private static int _intervalSeconds = 2;

    /// <summary>Raised on the UI thread after every sample (the snapshot is also in <see cref="Last"/>).</summary>
    public static event Action<MonitorSnapshot>? Updated;

    /// <summary>The newest snapshot (null before the first sample).</summary>
    public static MonitorSnapshot? Last { get; private set; }

    /// <summary>The settings of the first member.</summary>
    public static IAppSettings? App => _app;

    public static Dispatcher? Ui => _ui;

    public static IFeatureRuntime? Runtime => _runtime;

    /// <summary>True when the catalog monitor feature is installed.</summary>
    public static bool Installed(string featureId) => _runtime?.IsAvailable(featureId) ?? false;

    /// <summary>The history of a series (oldest first).</summary>
    public static IReadOnlyList<double> Series(string key)
    {
        lock (History)
        {
            return History.TryGetValue(key, out var list) ? [.. list] : [];
        }
    }

    // ------------------------------------------------------------------------------------------------ membership

    /// <summary>A monitor feature started.</summary>
    public static void Acquire(IFeatureContext context)
    {
        lock (Gate)
        {
            if (Members.Count == 0)
            {
                _ui = context.Ui;
                _app = context.App;
                _runtime = context.Runtime;
                _log = context.Log;
                _intervalSeconds = Interval();
                context.App.Changed += OnSetting;
            }

            Members.Add(context);
        }

        Reevaluate();
    }

    /// <summary>A monitor feature stopped.</summary>
    public static void Release(IFeatureContext context)
    {
        lock (Gate)
        {
            Members.Remove(context);
            if (Members.Count == 0)
            {
                context.App.Changed -= OnSetting;
            }
        }

        Reevaluate();
        if (Members.Count == 0)
        {
            Stop();
            _ui = null;
            _app = null;
            _runtime = null;
        }
    }

    /// <summary>Registers what a visible surface needs; dispose the lease when it goes away.</summary>
    public static IDisposable Foreground(Needs needs)
    {
        Lease? lease = null;
        lease = new Lease(needs, () =>
        {
            lock (Gate)
            {
                Leases.Remove(lease!);
            }

            Reevaluate();
        });
        lock (Gate)
        {
            Leases.Add(lease);
        }

        Reevaluate();
        _sampler?.Invalidate();
        return lease;
    }

    /// <summary>Sets what tray metrics and alerts need while no surface is visible.</summary>
    public static void SetBackground(string owner, Needs needs)
    {
        lock (Gate)
        {
            if (Backgrounds.GetValueOrDefault(owner) == needs)
            {
                return;
            }

            Backgrounds[owner] = needs;
        }

        Reevaluate();
    }

    private static Needs BackgroundNeeds()
    {
        lock (Gate)
        {
            return Backgrounds.Values.Aggregate(Needs.None, (acc, n) => acc | n);
        }
    }

    // ------------------------------------------------------------------------------------------------ run loop

    private static void OnSetting(string key)
    {
        if (key == "monitorIntervalSeconds")
        {
            var interval = Interval();
            if (interval != _intervalSeconds)
            {
                _intervalSeconds = interval;
                _ui?.BeginInvoke(() =>
                {
                    Stop();
                    Reevaluate();
                });
            }
        }
    }

    private static int Interval()
    {
        var v = _app?.Get<int>("monitorIntervalSeconds", 2) ?? 2;
        return v is 1 or 2 or 5 ? v : 2;
    }

    private static Needs Mask(Needs needs)
    {
        var allowed = Needs.None;
        if (Installed("monitor-cpu"))
        {
            allowed |= Needs.Cpu | Needs.CpuCores | Needs.CpuTemp;
        }

        if (Installed("monitor-gpu"))
        {
            allowed |= Needs.Gpu | Needs.GpuTemp;
        }

        if (Installed("monitor-memory"))
        {
            allowed |= Needs.Memory;
        }

        if (Installed("monitor-network"))
        {
            allowed |= Needs.Network;
        }

        if (Installed("monitor-disk"))
        {
            allowed |= Needs.Disk | Needs.DiskSpace | Needs.Smart;
        }

        if (Installed("monitor-power"))
        {
            allowed |= Needs.Power | Needs.PowerDraw | Needs.Peripherals;
        }

        if (Installed("connected-devices"))
        {
            allowed |= Needs.Devices;
        }

        // Uptime is cheap and needs no feature; the temperatures also follow their own gates.
        allowed |= Needs.Uptime;
        return needs & allowed;
    }

    private static void Reevaluate()
    {
        Needs foreground;
        lock (Gate)
        {
            foreground = Leases.Aggregate(Needs.None, (acc, l) => acc | l.Needs);
        }

        var wanted = Mask(foreground | BackgroundNeeds());
        lock (Gate)
        {
            if (Members.Count == 0 || wanted == Needs.None)
            {
                Stop();
                return;
            }

            if (_timer is null)
            {
                _generation++;
                var generation = _generation;
                _sampler ??= new SystemSampler();
                _timer = new Timer(_ => Tick(generation), null, 0, _intervalSeconds * 1000);
            }
        }
    }

    private static void Stop()
    {
        Timer? timer;
        lock (Gate)
        {
            timer = _timer;
            _timer = null;
            _generation++;
        }

        timer?.Dispose();
        lock (Gate)
        {
            _sampler?.Dispose();
            _sampler = null;
        }

        lock (History)
        {
            History.Clear();
        }

        Last = null;
    }

    private static void Tick(int generation)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            Needs foreground;
            lock (Gate)
            {
                foreground = Leases.Aggregate(Needs.None, (acc, l) => acc | l.Needs);
            }

            var needs = Mask(foreground | BackgroundNeeds());
            // The temperatures follow their parent metric only while something shows them.
            MonitorSnapshot snapshot;
            lock (Gate)
            {
                if (_sampler is null || generation != _generation)
                {
                    return;
                }

                snapshot = _sampler.Sample(needs, foreground == Needs.None);
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
            _log?.Error("System sampler failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static void Publish(MonitorSnapshot s)
    {
        Last = s;
        var memoryApp = _app?.Get<string>("monitorMemoryMetric") == "app";
        if (s.Cpu is { } cpu)
        {
            Push("cpu", cpu);
        }

        if (s.Gpu is { } gpu)
        {
            Push("gpu", gpu);
        }

        if (s.Memory is { } mem)
        {
            Push("mem", mem.Total == 0 ? 0 : (memoryApp ? mem.App : mem.Used) * 100.0 / mem.Total);
        }

        if (s.NetKnown)
        {
            Push("down", s.NetDown);
            Push("up", s.NetUp);
        }

        if (s.DiskRead > 0 || s.DiskWrite > 0 || Last.DiskByLetter.Count > 0)
        {
            Push("diskR", s.DiskRead);
            Push("diskW", s.DiskWrite);
        }

        if (s.Power is { HasBattery: true } p)
        {
            Push("battery", p.Percent);
            Push("power", p.DischargeWatts ?? p.ChargeWatts ?? 0);
        }

        Updated?.Invoke(s);
    }

    private static void Push(string key, double value)
    {
        lock (History)
        {
            if (!History.TryGetValue(key, out var list))
            {
                History[key] = list = [];
            }

            list.Add(value);
            if (list.Count > HistoryLength)
            {
                list.RemoveAt(0);
            }
        }
    }

    /// <summary>Takes a sample right now (a view that just opened should not wait a whole interval).</summary>
    public static void RequestNow()
    {
        var generation = _generation;
        if (_timer is not null)
        {
            Task.Run(() => Tick(generation));
        }
    }
}
