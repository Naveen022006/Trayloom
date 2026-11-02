// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal sealed record PortEntry(string Protocol, string LocalAddress, int Port, string RemoteAddress, string State, int Pid, string Process);

/// <summary>Reads the TCP/UDP tables (IPv4 and IPv6) with the owning process of every endpoint.</summary>
internal static class PortTable
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;

    private static readonly string[] TcpStates =
    [
        "?", "Closed", "Listen", "SYN sent", "SYN received", "Established", "FIN wait 1", "FIN wait 2",
        "Close wait", "Closing", "Last ACK", "Time wait", "Delete TCB",
    ];

    public static List<PortEntry> Read(bool listeningOnly)
    {
        var names = new Dictionary<int, string>();
        string NameOf(int pid)
        {
            if (names.TryGetValue(pid, out var n))
            {
                return n;
            }

            try
            {
                using var p = Process.GetProcessById(pid);
                n = p.ProcessName;
            }
            catch
            {
                n = pid == 0 ? "System Idle" : pid == 4 ? "System" : "(unknown)";
            }

            names[pid] = n;
            return n;
        }

        var list = new List<PortEntry>();
        foreach (var family in new[] { AfInet, AfInet6 })
        {
            var v6 = family == AfInet6;
            ReadTable(family, tcp: true, buffer: (ptr, count) =>
            {
                var rowSize = v6 ? 56 : 24;
                for (var i = 0; i < count; i++)
                {
                    var row = ptr + 4 + i * rowSize;
                    int state, pid, localPort, remotePort;
                    string local, remote;
                    if (v6)
                    {
                        local = Address6(row, 0, Marshal.ReadInt32(row, 16));
                        localPort = PortOf(Marshal.ReadInt32(row, 20));
                        remote = Address6(row, 24, Marshal.ReadInt32(row, 40));
                        remotePort = PortOf(Marshal.ReadInt32(row, 44));
                        state = Marshal.ReadInt32(row, 48);
                        pid = Marshal.ReadInt32(row, 52);
                    }
                    else
                    {
                        state = Marshal.ReadInt32(row, 0);
                        local = Address4(Marshal.ReadInt32(row, 4));
                        localPort = PortOf(Marshal.ReadInt32(row, 8));
                        remote = Address4(Marshal.ReadInt32(row, 12));
                        remotePort = PortOf(Marshal.ReadInt32(row, 16));
                        pid = Marshal.ReadInt32(row, 20);
                    }

                    if (listeningOnly && state != 2)
                    {
                        continue;
                    }

                    var stateName = state >= 0 && state < TcpStates.Length ? TcpStates[state] : state.ToString();
                    list.Add(new PortEntry(v6 ? "TCP6" : "TCP", local, localPort, state == 2 ? string.Empty : $"{remote}:{remotePort}", stateName, pid, NameOf(pid)));
                }
            });

            ReadTable(family, tcp: false, buffer: (ptr, count) =>
            {
                var rowSize = v6 ? 28 : 12;
                for (var i = 0; i < count; i++)
                {
                    var row = ptr + 4 + i * rowSize;
                    string local;
                    int port, pid;
                    if (v6)
                    {
                        local = Address6(row, 0, Marshal.ReadInt32(row, 16));
                        port = PortOf(Marshal.ReadInt32(row, 20));
                        pid = Marshal.ReadInt32(row, 24);
                    }
                    else
                    {
                        local = Address4(Marshal.ReadInt32(row, 0));
                        port = PortOf(Marshal.ReadInt32(row, 4));
                        pid = Marshal.ReadInt32(row, 8);
                    }

                    list.Add(new PortEntry(v6 ? "UDP6" : "UDP", local, port, string.Empty, "Bound", pid, NameOf(pid)));
                }
            });
        }

        return list;
    }

    private static void ReadTable(int family, bool tcp, Action<nint, int> buffer)
    {
        var size = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var ptr = Marshal.AllocHGlobal(Math.Max(size, 16));
            try
            {
                var result = tcp
                    ? N.GetExtendedTcpTable(ptr, ref size, true, family, TcpTableOwnerPidAll, 0)
                    : N.GetExtendedUdpTable(ptr, ref size, true, family, UdpTableOwnerPid, 0);
                if (result == 0)
                {
                    buffer(ptr, Marshal.ReadInt32(ptr));
                    return;
                }

                if (result != 122)
                {
                    return; // anything but INSUFFICIENT_BUFFER: give up on this table
                }

                size += 4096;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    private static int PortOf(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    private static string Address4(int raw) => new IPAddress((uint)raw & 0xFFFFFFFF).ToString();

    private static string Address6(nint row, int offset, int scope)
    {
        var bytes = new byte[16];
        Marshal.Copy(row + offset, bytes, 0, 16);
        return new IPAddress(bytes, scope).ToString();
    }
}

/// <summary>Lists listening ports with their owning process and lets you end the owner.</summary>
public sealed class PortManagerFeature : ToolFeatureBase
{
    private PortManagerWindow? _window;

    public override string Id => "port-manager";

    public override string Name => "Port manager";

    public override string Description => "See which program is listening on every TCP and UDP port and free a port by ending its owner.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Open port manager", () => Safe(Open), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Open);

    protected override void OnStop()
    {
        _window?.Close();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var open = UiKit.Btn("Open port manager", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open port manager", null, "Optional global shortcut.")
            .AddToggle("showAll", "Show all TCP connections", false, "By default only listening TCP ports (plus bound UDP ports) are listed.");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new PortManagerWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class PortManagerWindow : Window
{
    private readonly IFeatureContext _ctx;
    private readonly DataGrid _grid = new();
    private readonly TextBox _search = new() { MinWidth = 260 };
    private readonly TextBlock _status = UiKit.Muted(string.Empty);
    private readonly CheckBox _all = new() { Content = "All TCP connections", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer;
    private List<PortEntry> _entries = new();
    private bool _refreshing;

    public PortManagerWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Port manager";
        Width = 900;
        Height = 600;
        MinWidth = 600;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _all.IsChecked = ctx.Settings.Get("showAll", false);
        _all.Click += (_, _) =>
        {
            ctx.Settings.Set("showAll", _all.IsChecked == true);
            _ = RefreshAsync();
        };

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Protocol", Binding = new Binding(nameof(PortEntry.Protocol)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Port", Binding = new Binding(nameof(PortEntry.Port)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Local address", Binding = new Binding(nameof(PortEntry.LocalAddress)), Width = new DataGridLength(170) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Binding(nameof(PortEntry.State)), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Remote", Binding = new Binding(nameof(PortEntry.RemoteAddress)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PID", Binding = new Binding(nameof(PortEntry.Pid)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Process", Binding = new Binding(nameof(PortEntry.Process)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        _search.TextChanged += (_, _) => Apply();
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        searchRow.Children.Add(UiKit.Glyph("", 14, ThemeKeys.TextMuted));
        _search.Margin = new Thickness(8, 0, 0, 0);
        searchRow.Children.Add(_search);
        searchRow.Children.Add(_all);
        searchRow.Children.Add(new Border { Width = 16 });
        searchRow.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(UiKit.Btn("End owner process", () => KillOwner(false), true, ""));
        buttons.Children.Add(UiKit.Btn("End owner tree", () => KillOwner(true)));
        buttons.Children.Add(UiKit.Btn("Open in browser", OpenInBrowser, false, ""));
        buttons.Children.Add(UiKit.Btn("Copy port", () =>
        {
            if (_grid.SelectedItem is PortEntry e)
            {
                Clipboard.SetText(e.Port.ToString());
            }
        }));
        buttons.Children.Add(UiKit.Btn("Refresh", () => _ = RefreshAsync(), false, ""));

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(searchRow);
        layout.Children.Add(buttons);
        layout.Children.Add(_grid);
        Content = layout;

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RefreshAsync(), Dispatcher);
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
            _search.Focus();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var listeningOnly = _all.IsChecked != true;
            _entries = await Task.Run(() => PortTable.Read(listeningOnly));
            Apply();
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("Port refresh failed: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply()
    {
        var q = _search.Text.Trim();
        var selected = _grid.SelectedItem as PortEntry;
        var filtered = _entries
            .Where(e => q.Length == 0
                || e.Port.ToString().Contains(q)
                || e.Process.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Pid.ToString() == q
                || e.LocalAddress.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Port)
            .ThenBy(e => e.Protocol)
            .ToList();
        _grid.ItemsSource = filtered;
        if (selected is not null)
        {
            _grid.SelectedItem = filtered.FirstOrDefault(e => e.Pid == selected.Pid && e.Port == selected.Port && e.Protocol == selected.Protocol && e.LocalAddress == selected.LocalAddress);
        }

        _status.Text = $"{filtered.Count} endpoints";
    }

    private void KillOwner(bool tree)
    {
        if (_grid.SelectedItem is not PortEntry entry)
        {
            MessageBox.Show(this, "Select a port first.", "Port manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ProcessKiller.Refuse(entry.Pid, entry.Process) is { } reason)
        {
            MessageBox.Show(this, reason, "Port manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var text = $"Port {entry.Port} ({entry.Protocol}) is used by \"{entry.Process}\" (PID {entry.Pid}).\r\n\r\nEnd that process{(tree ? " and everything it started" : string.Empty)}?";
        if (MessageBox.Show(this, text, "End owner process", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (ProcessKiller.Kill(entry.Pid, entry.Process, tree) is { } error)
        {
            MessageBox.Show(this, error, "Port manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _ = RefreshAsync();
    }

    private void OpenInBrowser()
    {
        if (_grid.SelectedItem is PortEntry { Protocol: "TCP" or "TCP6" } entry)
        {
            Process.Start(new ProcessStartInfo($"http://localhost:{entry.Port}/") { UseShellExecute = true });
        }
    }
}
