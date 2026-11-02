// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinKnife.Features.Island;

/// <summary>Light-weight system readings for the System page (sampled only while it is visible).</summary>
internal sealed class SystemSamples
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private ulong _idle;
    private ulong _total;
    private long _rx;
    private long _tx;
    private DateTime _netAt;

    public double Cpu { get; private set; }

    public double Memory { get; private set; }

    public double DiskFree { get; private set; }

    public long DiskFreeBytes { get; private set; }

    public double RxRate { get; private set; }

    public double TxRate { get; private set; }

    public void Sample()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = idle.Value;
            var t = kernel.Value + user.Value;
            if (_total != 0 && t > _total)
            {
                Cpu = Math.Clamp(1 - ((double)(i - _idle) / (t - _total)), 0, 1);
            }

            _idle = i;
            _total = t;
        }

        var m = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            Memory = m.MemoryLoad / 100.0;
        }

        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var d = new DriveInfo(root);
            DiskFreeBytes = d.AvailableFreeSpace;
            DiskFree = (double)d.AvailableFreeSpace / Math.Max(1, d.TotalSize);
        }
        catch
        {
        }

        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var s = nic.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }

            var now = DateTime.UtcNow;
            if (_netAt != default)
            {
                var dt = Math.Max(0.1, (now - _netAt).TotalSeconds);
                RxRate = Math.Max(0, (rx - _rx) / dt);
                TxRate = Math.Max(0, (tx - _tx) / dt);
            }

            _rx = rx;
            _tx = tx;
            _netAt = now;
        }
        catch
        {
        }
    }

    public static string Rate(double bytes, bool bits)
    {
        var v = bits ? bytes * 8 : bytes;
        string[] u = bits ? ["bps", "Kbps", "Mbps", "Gbps"] : ["B/s", "KB/s", "MB/s", "GB/s"];
        var i = 0;
        while (v >= 1000 && i < u.Length - 1)
        {
            v /= 1000;
            i++;
        }

        return $"{(v >= 100 || i == 0 ? v.ToString("0") : v.ToString("0.#"))} {u[i]}";
    }
}

internal sealed class SystemPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly SystemSamples _s = new();
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(Action Update, FrameworkElement Card)> _cards = [];
    private double _w = 400;
    private double _h = 264;

    public SystemPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        _tick.Tick += (_, _) => Update();
    }

    public override string ModuleId => "system";

    public override FrameworkElement View => _view;

    public override bool HasTopRow => false;

    public override bool ScrollsContent => true;

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        var inset = Math.Ceiling(width * 0.015) + 1;
        var cap = Math.Max(1, (int)Math.Floor((width - (2 * inset) + 10) / 138.0));
        var rows = (int)Math.Ceiling(_cards.Count / (double)cap);
        return Math.Min(budget, Math.Max(80, (rows * 72) + ((rows - 1) * 10) + (2 * inset)));
    }

    public override void Shown()
    {
        _s.Sample();
        _tick.Start();
    }

    public override void Hidden() => _tick.Stop();

    private void Build()
    {
        _view.Children.Clear();
        _cards.Clear();
        var rt = Core.Ctx.Runtime;
        if (rt.IsAvailable("monitor-cpu"))
        {
            Add(G.Cpu, "CPU", () => ($"{(int)Math.Round(_s.Cpu * 100)}%", null, _s.Cpu, _s.Cpu >= 0.85));
        }

        if (rt.IsAvailable("monitor-memory"))
        {
            Add(G.Memory, "Memory", () => ($"{(int)Math.Round(_s.Memory * 100)}%", null, _s.Memory, _s.Memory >= 0.85));
        }

        if (rt.IsAvailable("monitor-power") && IslandNative.Power().HasBattery)
        {
            Add(G.Battery, "Battery", () =>
            {
                var (_, pct, plugged, _) = IslandNative.Power();
                return ($"{pct}%", null, pct / 100.0, !plugged && pct <= 20);
            });
        }

        if (rt.IsAvailable("monitor-network"))
        {
            Add(G.Network, "Network", () => ("\u2193 " + SystemSamples.Rate(_s.RxRate, false), "\u2191 " + SystemSamples.Rate(_s.TxRate, false), null, false));
        }

        if (rt.IsAvailable("monitor-disk"))
        {
            Add(G.Disk, "Available", () => (DownloadsPage.FormatBytes(_s.DiskFreeBytes), null, _s.DiskFree, _s.DiskFree < 0.10));
        }

        if (_cards.Count == 0)
        {
            _view.Children.Add(D.EmptyView(G.System, Strings.SensorsUnavailable));
            return;
        }

        var inset = Math.Ceiling(_w * 0.015) + 1;
        var cap = Math.Max(1, (int)Math.Floor((_w - (2 * inset) + 10) / 138.0));
        var rows = (int)Math.Ceiling(_cards.Count / (double)cap);
        var stack = new StackPanel { Margin = new Thickness(inset) };
        var index = 0;
        for (var r = 0; r < rows; r++)
        {
            var count = (_cards.Count / rows) + (r < _cards.Count % rows ? 1 : 0);
            var row = new Grid { Margin = new Thickness(0, r == 0 ? 0 : 10, 0, 0), Height = 72 };
            for (var c = 0; c < count; c++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var card = _cards[index++].Card;
                card.Margin = new Thickness(c == 0 ? 0 : 5, 0, c == count - 1 ? 0 : 5, 0);
                Grid.SetColumn(card, c);
                row.Children.Add(card);
            }

            stack.Children.Add(row);
        }

        _view.Children.Add(PageUi.VScroll(stack));
        Update();
    }

    private void Add(string glyph, string title, Func<(string Value, string? Detail, double? Meter, bool Attention)> read)
    {
        var card = new Border { Height = 72, MinWidth = 128, CornerRadius = new CornerRadius(18), Background = D.White(0.065), Padding = new Thickness(12, 0, 12, 0) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(D.I(glyph, 10, D.Secondary));
        var lt = D.T(title, 10, FontWeights.Medium, D.Secondary);
        lt.Margin = new Thickness(5, 0, 0, 0);
        label.Children.Add(lt);
        stack.Children.Add(label);
        var value = D.Digits("\u2026", 22, FontWeights.Medium);
        value.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        value.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(value);
        var detail = D.Digits("", 11, null, D.Secondary);
        detail.Margin = new Thickness(0, 6, 0, 0);
        var meter = D.MeterBar(0, null, 5);
        meter.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(detail);
        stack.Children.Add(meter);
        card.Child = stack;
        _cards.Add((() =>
        {
            var (v, d, m, attention) = read();
            value.Text = v;
            value.FontSize = d is null ? 22 : 15;
            detail.Text = d ?? "";
            detail.Visibility = d is null ? Visibility.Collapsed : Visibility.Visible;
            meter.Visibility = m is null ? Visibility.Collapsed : Visibility.Visible;
            if (m is { } mv)
            {
                meter.Value = mv;
                meter.Tint = attention ? D.Orange : Brushes.White;
            }
        }, card));
    }

    private void Update()
    {
        _s.Sample();
        foreach (var (update, _) in _cards)
        {
            update();
        }
    }
}
