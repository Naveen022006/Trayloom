// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;

namespace WinKnife.Features.SystemTools;

/// <summary>What a per-app list measures.</summary>
internal enum UsageKind
{
    Cpu,
    Gpu,
    Memory,
    Energy,
}

/// <summary>
/// "Apps using ..." lists: per-process CPU (time deltas), GPU (engine counters per pid), memory (working set) and a CPU+GPU energy estimate,
/// grouped by app. The first call after a pause only primes the baseline and returns null ("Measuring...").
/// </summary>
internal sealed class ProcessSampler : IDisposable
{
    private readonly UsageKind _kind;
    private Dictionary<int, TimeSpan> _cpuPrev = [];
    private DateTime _at = DateTime.MinValue;
    private PdhQuery? _gpuQuery;
    private nint _gpuCounter;

    public ProcessSampler(UsageKind kind) => _kind = kind;

    /// <summary>Up to <paramref name="limit"/> rows, busiest first; null while the baseline is being taken.</summary>
    public List<ProcessUsage>? Sample(int limit = 15)
    {
        var now = DateTime.UtcNow;
        var dt = _at == DateTime.MinValue ? 0 : (now - _at).TotalSeconds;
        var cores = Math.Max(1, Environment.ProcessorCount);
        var needCpu = _kind is UsageKind.Cpu or UsageKind.Energy;
        var needGpu = _kind is UsageKind.Gpu or UsageKind.Energy;
        var cpuNow = new Dictionary<int, TimeSpan>();
        var gpu = needGpu ? ReadGpu() : [];
        var rows = new Dictionary<string, (string Name, int Pid, double Value, string Path)>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            return [];
        }

        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    var pid = p.Id;
                    if (pid <= 4)
                    {
                        continue;
                    }

                    double value = 0;
                    if (needCpu)
                    {
                        var total = p.TotalProcessorTime;
                        cpuNow[pid] = total;
                        if (dt > 0 && _cpuPrev.TryGetValue(pid, out var prev))
                        {
                            value += Math.Max(0, (total - prev).TotalSeconds) / (dt * cores) * 100.0;
                        }
                    }

                    if (needGpu && gpu.TryGetValue(pid, out var g))
                    {
                        value += _kind == UsageKind.Energy ? g * 0.5 : g;
                    }

                    if (_kind == UsageKind.Memory)
                    {
                        value = p.WorkingSet64;
                    }

                    if (value <= 0)
                    {
                        continue;
                    }

                    var key = p.ProcessName;
                    var path = string.Empty;
                    if (rows.TryGetValue(key, out var existing))
                    {
                        rows[key] = (existing.Name, existing.Pid, existing.Value + value, existing.Path);
                    }
                    else
                    {
                        rows[key] = (key, pid, value, path);
                    }
                }
                catch (Exception)
                {
                    // Protected process: no access.
                }
            }
        }

        _cpuPrev = cpuNow;
        _at = now;
        if (dt == 0 && _kind != UsageKind.Memory)
        {
            return null;
        }

        var top = rows.Values.OrderByDescending(r => r.Value).Take(limit).ToList();
        var result = new List<ProcessUsage>();
        foreach (var r in top)
        {
            var (_, name, icon) = AppIdentity.Resolve((uint)r.Pid, false);
            var display = _kind == UsageKind.Memory ? Fmt.BytesBinary(r.Value) : string.Create(CultureInfo.CurrentCulture, $"{Math.Min(100, r.Value):0.0}%");
            result.Add(new ProcessUsage(string.IsNullOrWhiteSpace(name) ? r.Name : name, r.Pid, r.Value, display, icon));
        }

        if (_kind == UsageKind.Energy)
        {
            result = [.. result.Where(r => r.Value >= 0.5)];
        }

        return result;
    }

    public void Dispose()
    {
        _gpuQuery?.Dispose();
        _gpuQuery = null;
    }

    private Dictionary<int, double> ReadGpu()
    {
        var result = new Dictionary<int, double>();
        try
        {
            if (_gpuQuery is null)
            {
                _gpuQuery = new PdhQuery();
                _gpuCounter = _gpuQuery.Add(@"\GPU Engine(*)\Utilization Percentage");
                _gpuQuery.Collect();
                return result;
            }

            _gpuQuery.Collect();
            foreach (var (instance, value) in _gpuQuery.Array(_gpuCounter))
            {
                // pid_1234_luid_..._engtype_3D
                if (!instance.StartsWith("pid_", StringComparison.Ordinal))
                {
                    continue;
                }

                var end = instance.IndexOf('_', 4);
                if (end > 4 && int.TryParse(instance.AsSpan(4, end - 4), out var pid))
                {
                    result[pid] = result.GetValueOrDefault(pid) + value;
                }
            }
        }
        catch (Exception)
        {
        }

        return result;
    }
}
