// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Reflection;
using System.Windows.Interop;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.App;

/// <summary>
/// Developer aid (TRAYLOOM_DEV=1 only): once a minute logs one <c>[perf]</c> line with how late a Background-priority timer
/// fired on the UI thread (the lag a user feels), how busy the UI thread was, the managed heap and GC counts, the process
/// memory, and the UI operations that took the most time. It hooks the dispatcher, so it never runs in a normal copy.
/// </summary>
internal sealed class DevPerfProbe : IDisposable
{
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(60);

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly DispatcherTimer _probe;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stack<(DispatcherOperation Operation, long Start)> _running = new();
    private readonly Dictionary<string, (double Ms, int Count)> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<MethodInfo, string> _names = [];
    private readonly FieldInfo? _methodField;
    private readonly FieldInfo? _argsField;
    private readonly FieldInfo? _tickField;
    private long _lastProbe;
    private long _lastReport;
    private double _lateSum;
    private double _lateMax;
    private int _lateCount;
    private double _busyMs;
    private double _slowestMs;
    private string _slowestName = "";
    private DateTime _lateMaxAt;
    private long _messageStart;
    private int _messageId;
    private double _longestMessageMs;
    private int _longestMessageId;
    private int _gen0;
    private int _gen1;
    private int _gen2;
    private bool _disposed;

    private DevPerfProbe(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;

        // Internal WPF fields, read only to name the slow operations; when a WPF update renames them the line just says "?".
        var operationFields = typeof(DispatcherOperation).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        _methodField = operationFields.FirstOrDefault(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
        _argsField = operationFields.FirstOrDefault(f => f.FieldType == typeof(object));
        _tickField = typeof(DispatcherTimer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.FieldType == typeof(EventHandler));

        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _lastProbe = _clock.ElapsedTicks;
        _lastReport = _lastProbe;
        _ui.Hooks.OperationStarted += OnStarted;
        _ui.Hooks.OperationCompleted += OnFinished;
        _ui.Hooks.OperationAborted += OnFinished;

        // Input and window messages are handled in window procedures, outside any dispatcher operation: the longest message
        // (from its arrival to the next one or to the loop going idle) catches a stall there too.
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
        ComponentDispatcher.ThreadIdle += OnIdle;
        _probe = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = ProbeEvery };
        _probe.Tick += (_, _) => OnProbe();
        _probe.Start();
        _log.Info("[perf] probe on: one line a minute (TRAYLOOM_DEV=1)");
    }

    /// <summary>Starts the probe when TRAYLOOM_DEV=1; otherwise returns null and costs nothing.</summary>
    public static DevPerfProbe? StartIfDev(Dispatcher ui, ILog log) =>
        Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1" ? new DevPerfProbe(ui, log) : null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _probe.Stop();
        _ui.Hooks.OperationStarted -= OnStarted;
        _ui.Hooks.OperationCompleted -= OnFinished;
        _ui.Hooks.OperationAborted -= OnFinished;
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        ComponentDispatcher.ThreadIdle -= OnIdle;
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        EndMessage();
        _messageStart = _clock.ElapsedTicks;
        _messageId = msg.message;
    }

    private void OnIdle(object? sender, EventArgs e) => EndMessage();

    private void EndMessage()
    {
        if (_messageStart == 0)
        {
            return;
        }

        var ms = (_clock.ElapsedTicks - _messageStart) * 1000.0 / Stopwatch.Frequency;
        _messageStart = 0;
        if (ms > _longestMessageMs)
        {
            _longestMessageMs = ms;
            _longestMessageId = _messageId;
        }
    }

    private void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        _running.Push((e.Operation, _clock.ElapsedTicks));
    }

    private void OnFinished(object? sender, DispatcherHookEventArgs e)
    {
        // Operations nest only through nested message loops, so the finished one is on top; anything above it was abandoned.
        while (_running.Count > 0)
        {
            var (operation, start) = _running.Pop();
            if (!ReferenceEquals(operation, e.Operation))
            {
                continue;
            }

            var ms = (_clock.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
            _busyMs += ms;
            var name = NameOf(operation);
            _byName.TryGetValue(name, out var sum);
            _byName[name] = (sum.Ms + ms, sum.Count + 1);
            if (ms > _slowestMs)
            {
                // The one operation that held the UI thread longest is what a user felt as a hitch.
                _slowestMs = ms;
                _slowestName = name;
            }

            return;
        }
    }

    private void OnProbe()
    {
        var now = _clock.ElapsedTicks;
        var late = (now - _lastProbe) * 1000.0 / Stopwatch.Frequency - ProbeEvery.TotalMilliseconds;
        _lastProbe = now;
        late = Math.Max(0, late);
        _lateSum += late;
        if (late > _lateMax)
        {
            _lateMax = late;
            _lateMaxAt = DateTime.Now;
        }

        _lateCount++;

        var seconds = (now - _lastReport) / (double)Stopwatch.Frequency;
        if (seconds >= ReportEvery.TotalSeconds)
        {
            Report(seconds);
            _lastReport = now;
        }
    }

    private void Report(double seconds)
    {
        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);
        var info = GC.GetGCMemoryInfo();
        long privateMb;
        long workingMb;
        int threads;
        using (var self = Process.GetCurrentProcess())
        {
            privateMb = self.PrivateMemorySize64 >> 20;
            workingMb = self.WorkingSet64 >> 20;
            threads = self.Threads.Count;
        }

        var top = string.Join(", ", _byName
            .OrderByDescending(p => p.Value.Ms)
            .Take(8)
            .Select(p => $"{p.Key} {p.Value.Ms / seconds:0.0} ms/s x{p.Value.Count / seconds:0.#}/s"));
        _log.Info(
            $"[perf] ui late avg {_lateSum / Math.Max(1, _lateCount):0.0} ms max {_lateMax:0} ms at {_lateMaxAt:HH:mm:ss}, ui busy {_busyMs / seconds:0.0} ms/s, "
            + $"slowest {_slowestName} {_slowestMs:0} ms, longest message 0x{_longestMessageId:X4} {_longestMessageMs:0} ms; "
            + $"heap {GC.GetTotalMemory(false) >> 20} MB (committed {info.TotalCommittedBytes >> 20} MB), gc +{g0 - _gen0}/+{g1 - _gen1}/+{g2 - _gen2}; "
            + $"private {privateMb} MB, ws {workingMb} MB, threads {threads}; top: {top}");
        _gen0 = g0;
        _gen1 = g1;
        _gen2 = g2;
        _lateSum = 0;
        _lateMax = 0;
        _lateCount = 0;
        _busyMs = 0;
        _slowestMs = 0;
        _slowestName = "";
        _longestMessageMs = 0;
        _longestMessageId = 0;
        _byName.Clear();
    }

    /// <summary>A short name for an operation: the timer's Tick handler for timers, else the queued delegate's method.</summary>
    private string NameOf(DispatcherOperation operation)
    {
        if (_methodField?.GetValue(operation) is not Delegate method)
        {
            return "?";
        }

        // A timer's tick is queued as a static callback with the timer as its argument; an await resumed on the UI thread or a
        // posted callback carries the real work as its argument too.
        var args = _argsField?.GetValue(operation);
        var timer = method.Target as DispatcherTimer ?? args as DispatcherTimer;
        if (timer is not null && _tickField?.GetValue(timer) is Delegate tick)
        {
            return "timer " + Describe(tick.GetInvocationList()[0]);
        }

        if (args is Delegate inner)
        {
            return Describe(inner);
        }

        return Describe(method.Method);
    }

    private string Describe(Delegate work)
    {
        // An async method's continuation targets its state machine box: the state machine type names the method.
        var box = work.Target?.GetType();
        if (box is { IsGenericType: true } && box.Name.Contains("StateMachineBox", StringComparison.Ordinal))
        {
            var machine = box.GetGenericArguments()[^1];
            return "async " + (machine.DeclaringType?.Name ?? "?") + "." + machine.Name;
        }

        return Describe(work.Method);
    }

    private string Describe(MethodInfo method)
    {
        if (!_names.TryGetValue(method, out var name))
        {
            // Lambdas live in compiler classes like Outer+<>c__DisplayClass5_0: the outer type is the useful part.
            var type = method.DeclaringType;
            while (type is not null && type.Name.Contains('<') && type.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }

            name = (type?.Name ?? "?") + "." + method.Name;
            _names[method] = name;
        }

        return name;
    }
}
