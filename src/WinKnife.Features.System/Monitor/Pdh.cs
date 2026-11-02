// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace WinKnife.Features.SystemTools;

/// <summary>Thin wrapper over the Performance Data Helper API (pdh.dll); avoids the PerformanceCounter NuGet package.</summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint FmtDouble = 0x00000200;
    private const uint FmtNoCap100 = 0x00008000;
    private const uint MoreData = 0x800007D2;

    private nint _query;

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0)
        {
            _query = 0;
        }
    }

    public bool IsOpen => _query != 0;

    /// <summary>Adds a counter (English path, wildcards allowed). Returns 0 when the counter does not exist.</summary>
    public nint Add(string path)
    {
        if (_query == 0)
        {
            return 0;
        }

        return PdhAddEnglishCounterW(_query, path, 0, out var counter) == 0 ? counter : 0;
    }

    public bool Collect() => _query != 0 && PdhCollectQueryData(_query) == 0;

    public double? Value(nint counter)
    {
        if (counter == 0 || PdhGetFormattedCounterValue(counter, FmtDouble | FmtNoCap100, out _, out var value) != 0 || value.CStatus != 0)
        {
            return null;
        }

        return value.DoubleValue;
    }

    /// <summary>Values of a wildcard counter keyed by instance name.</summary>
    public List<(string Instance, double Value)> Array(nint counter)
    {
        var result = new List<(string, double)>();
        if (counter == 0)
        {
            return result;
        }

        uint size = 0;
        uint count = 0;
        var status = PdhGetFormattedCounterArrayW(counter, FmtDouble | FmtNoCap100, ref size, ref count, 0);
        if (status != MoreData || size == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, FmtDouble | FmtNoCap100, ref size, ref count, buffer);
            if (status != 0)
            {
                return result;
            }

            var itemSize = Marshal.SizeOf<PdhItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PdhItem>(buffer + (i * itemSize));
                if (item.Value.CStatus != 0)
                {
                    continue;
                }

                result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            PdhCloseQuery(_query);
            _query = 0;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhItem
    {
        public nint Name;
        public PdhValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, ref uint itemCount, nint buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);
}
