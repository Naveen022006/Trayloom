// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>One listening TCP socket.</summary>
internal sealed record PortEntry(int Port, string Protocol, string Address, int Pid, string ProcessName, long StartedAt)
{
    public string Id => $"{Protocol}-{Port}-{Pid}-{Address}";

    /// <summary>"*:3000", "127.0.0.1:3000", "[::1]:3000".</summary>
    public string Endpoint => Address;

    public bool ListensOnAllInterfaces
    {
        get
        {
            var colon = Address.LastIndexOf(':');
            var host = colon < 0 ? Address : Address[..colon];
            host = host.Trim('[', ']');
            return host is "*" or "0.0.0.0" or "::";
        }
    }

    public string? BrowserUrl
    {
        get
        {
            var colon = Address.LastIndexOf(':');
            if (Protocol != "TCP" || Port is < 1 or > 65535 || colon < 0)
            {
                return null;
            }

            var host = Address[..colon];
            host = host switch { "*" => "localhost", "0.0.0.0" => "127.0.0.1", "[::]" or "::" => "[::1]", _ => host };
            return $"http://{host}:{Port}";
        }
    }

    public bool IsProtected => KillService.IsProtected(Pid, ProcessName, StartedAt);
}

/// <summary>Lists TCP LISTEN sockets with <c>GetExtendedTcpTable</c> (spec 6.5.6).</summary>
internal static class PortList
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;

    public static List<PortEntry>? Read()
    {
        try
        {
            var first = Table();
            if (first is null)
            {
                return null;
            }

            var before = first.Select(r => r.Pid).Distinct().ToDictionary(p => p, Win32.CreationTime);
            var second = Table() ?? first;
            var names = new Dictionary<int, string>();
            var result = new Dictionary<string, PortEntry>();
            foreach (var (pid, port, address) in second)
            {
                if (pid <= 0 && pid != 4 || port <= 0)
                {
                    continue;
                }

                var after = Win32.CreationTime(pid);
                var stable = before.TryGetValue(pid, out var created) && created == after && after != 0;
                if (!names.TryGetValue(pid, out var name))
                {
                    names[pid] = name = NameOf(pid);
                }

                var entry = new PortEntry(port, "TCP", address, pid, name, stable ? after : 0);
                result[$"TCP|{port}|{address}|{pid}"] = entry;
            }

            return result.Values.OrderBy(e => e.Port).ThenBy(e => e.ProcessName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static string NameOf(int pid)
    {
        if (pid == 4)
        {
            return Loc.T("System");
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "(unknown)";
        }
    }

    private static List<(int Pid, int Port, string Address)>? Table()
    {
        var rows = new List<(int, int, string)>();
        foreach (var family in new[] { AfInet, AfInet6 })
        {
            var size = 0;
            var result = Win32.GetExtendedTcpTable(0, ref size, true, family, TcpTableOwnerPidListener, 0);
            for (var attempt = 0; attempt < 4 && result == 122; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    result = Win32.GetExtendedTcpTable(buffer, ref size, true, family, TcpTableOwnerPidListener, 0);
                    if (result == 0)
                    {
                        var count = Marshal.ReadInt32(buffer);
                        var offset = 4;
                        for (var i = 0; i < count; i++)
                        {
                            if (family == AfInet)
                            {
                                var addr = (uint)Marshal.ReadInt32(buffer, offset + 4);
                                var port = NetworkPort(Marshal.ReadInt32(buffer, offset + 8));
                                var pid = Marshal.ReadInt32(buffer, offset + 20);
                                var ip = new IPAddress(addr);
                                var host = ip.ToString() == "0.0.0.0" ? "*" : ip.ToString();
                                rows.Add((pid, port, $"{host}:{port}"));
                                offset += 24;
                            }
                            else
                            {
                                var bytes = new byte[16];
                                Marshal.Copy(buffer + offset, bytes, 0, 16);
                                var scope = Marshal.ReadInt32(buffer, offset + 16);
                                var port = NetworkPort(Marshal.ReadInt32(buffer, offset + 20));
                                var pid = Marshal.ReadInt32(buffer, offset + 52);
                                var ip = new IPAddress(bytes, scope);
                                var text = ip.ToString();
                                var host = text == "::" ? "*" : $"[{text}]";
                                rows.Add((pid, port, $"{host}:{port}"));
                                offset += 56;
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            if (result != 0 && result != 122)
            {
                return null;
            }
        }

        return rows;
    }

    private static int NetworkPort(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
}

/// <summary>Port list + filter + terminate shared by the page and the panel.</summary>
internal sealed class PortManagerView : StackPanel
{
    private readonly bool _compact;
    private readonly Action? _relayout;
    private readonly Action<bool>? _keepOpen;
    private readonly ContentControl _body = new();
    private readonly TextBlock _openCount = UiKit.Text(Loc.T("0 open"), 9.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
    private readonly Spinner _spinner = new(12);
    private List<PortEntry> _entries = [];
    private string _query = string.Empty;
    private bool _loaded;
    private bool _failed;
    private bool _refreshing;

    public PortManagerView(bool compact, Action? relayout = null, Action<bool>? keepOpen = null)
    {
        _compact = compact;
        _relayout = relayout;
        _keepOpen = keepOpen;
        BuildChrome();
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private void BuildChrome()
    {
        var search = V.SearchField(Loc.T("Filter by port, process, or PID"), out var box, _compact ? 11 : 12, _compact ? 7 : 9, _compact ? 6 : 9);
        box.TextChanged += (_, _) =>
        {
            _query = box.Text.Trim().ToLowerInvariant();
            Render();
        };
        var refresh = V.IconButton(G.Refresh, Loc.T("Refresh"), () => _ = RefreshAsync(), 11, ThemeKeys.TextPrimary, _compact ? 24 : 22);
        _spinner.Visibility = Visibility.Collapsed;

        if (_compact)
        {
            var caption = V.Row(0, UiKit.Text(Loc.T("Your listening ports"), 10, FontWeights.Normal, ThemeKeys.TextSecondary));
            var head = V.Split(caption, _openCount);
            var fieldRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            fieldRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fieldRow.Children.Add(search);
            refresh.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(refresh, 1);
            fieldRow.Children.Add(refresh);
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(fieldRow);
            Children.Add(new PanelCard(stack));
            _body.Margin = new Thickness(0, 10, 0, 0);
            Children.Add(_body);
            return;
        }

        // The Settings page already draws the "Port Manager" heading: only the tools row here.
        var title = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(search);
        var tools = V.Row(6, _spinner, refresh);
        tools.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(tools, 1);
        title.Children.Add(tools);
        Children.Add(title);
        var card = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(6) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
        card.Child = _body;
        Children.Add(card);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        _spinner.Visibility = Visibility.Visible;
        var result = await Task.Run(PortList.Read).ConfigureAwait(true);
        _refreshing = false;
        _spinner.Visibility = Visibility.Collapsed;
        _loaded = true;
        if (result is null)
        {
            _failed = true;
        }
        else
        {
            _failed = false;
            _entries = result;
        }

        Render();
    }

    private void Render()
    {
        var filtered = string.IsNullOrEmpty(_query)
            ? _entries
            : _entries.Where(e => $"{e.Port} {e.ProcessName} {e.Pid} {e.Address}".ToLowerInvariant().Contains(_query)).ToList();
        _openCount.Text = Loc.F("{0} open", filtered.Count);
        var content = new StackPanel();
        if (_failed)
        {
            var warning = V.Label(Glyphs.Warning, Loc.T("Could not read listening ports. Try refreshing."), _compact ? 10.5 : 13, FontWeights.Normal, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary);
            foreach (var t in warning.Children.OfType<TextBlock>().Skip(1))
            {
                t.TextWrapping = TextWrapping.Wrap;
            }

            warning.Margin = new Thickness(_compact ? 10 : 14);
            content.Children.Add(new Border { Child = warning, MinHeight = _compact ? 72 : 120 });
        }
        else if (!_loaded)
        {
            var spinner = new Spinner(18) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new Border { Child = spinner, Height = _compact ? 72 : 160 });
        }
        else if (filtered.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = UiKit.Icon(G.Network, _compact ? 16 : 25, ThemeKeys.TextTertiary);
            empty.Children.Add(icon);
            var text = UiKit.Text(Loc.T("No listening ports found"), _compact ? 10.5 : 13, _compact ? FontWeights.Medium : FontWeights.Normal, ThemeKeys.TextSecondary);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 6, 0, 0);
            empty.Children.Add(text);
            var hint = V.Tertiary(Loc.T("Try refreshing or changing your search."), _compact ? 9.5 : 11.5);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 2, 0, 0);
            empty.Children.Add(hint);
            var host = new Border { Child = empty, Height = _compact ? 72 : 160 };
            if (_compact)
            {
                host.CornerRadius = new CornerRadius(10);
                host.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
            }

            content.Children.Add(host);
        }
        else
        {
            var list = new StackPanel();
            foreach (var entry in filtered)
            {
                var row = Row(entry);
                row.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : (_compact ? 6 : 2), 0, 0);
                list.Children.Add(row);
            }

            content.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = _compact ? 260 : 480,
                Content = list,
            });
        }

        _body.Content = content;
        _relayout?.Invoke();
    }

    private FrameworkElement Row(PortEntry entry)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(G.Network, 11, ThemeKeys.Accent, FontWeights.SemiBold);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 3, 0, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(_compact ? 4 : 8, 0, 8, 0) };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        var port = UiKit.Text(entry.Port.ToString(), _compact ? 11.5 : 12, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        port.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        line1.Children.Add(port);
        var proto = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(4, 1.5, 4, 1.5), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        proto.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        proto.Child = UiKit.Text(entry.Protocol, _compact ? 8.5 : 9, FontWeights.Bold, ThemeKeys.TextSecondary);
        line1.Children.Add(proto);
        if (entry.ListensOnAllInterfaces)
        {
            var badge = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(4, 1.5, 4, 1.5), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Listening on every network interface, so other devices on the network may be able to connect.") };
            badge.SetResourceReference(Border.BackgroundProperty, ThemeKeys.OrangeFill);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(UiKit.Icon(Glyphs.Globe, _compact ? 8.5 : 9, ThemeKeys.Orange, FontWeights.Bold));
            if (!_compact)
            {
                var label = UiKit.Text(Loc.T("All interfaces"), 9, FontWeights.Bold, ThemeKeys.Orange);
                label.Margin = new Thickness(2.5, 0, 0, 0);
                content.Children.Add(label);
            }

            badge.Child = content;
            System.Windows.Automation.AutomationProperties.SetName(badge, Loc.T("All interfaces"));
            line1.Children.Add(badge);
        }

        text.Children.Add(line1);
        text.Children.Add(UiKit.Text(entry.ProcessName, _compact ? 10.5 : 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var pid = UiKit.Text(Loc.F("PID {0}  •  {1}", entry.Pid, entry.Address), _compact ? 8.5 : 9, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
        pid.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        text.Children.Add(pid);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (Host.Runtime.IsAvailable("kill-process"))
        {
            var disabled = entry.StartedAt == 0 || entry.IsProtected;
            var kill = V.Mini(Loc.T("Kill"), () => Terminate(entry, false), null, _compact ? 10 : 10.5);
            kill.IsEnabled = !disabled;
            var bolt = V.Mini(string.Empty, () => Terminate(entry, true), G.Bolt, _compact ? 10 : 10.5);
            bolt.IsEnabled = !disabled;
            bolt.ToolTip = Loc.T("Force Kill");
            System.Windows.Automation.AutomationProperties.SetName(bolt, Loc.T("Force Kill"));
            var buttons = V.Row(4, kill, bolt);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
        }

        var border = new Border { CornerRadius = new CornerRadius(_compact ? 6 : 8), Padding = _compact ? new Thickness(6, 4, 6, 4) : new Thickness(8, 5, 8, 5), Child = grid };
        if (_compact)
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        }
        else
        {
            border.Background = Brushes.Transparent;
            border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        }

        var url = entry.BrowserUrl;
        var menu = new List<(string? Title, Action? Action, bool Enabled)>
        {
            (Loc.T("Copy port"), () => Copy(entry.Port.ToString()), true),
            (Loc.T("Copy PID"), () => Copy(entry.Pid.ToString()), true),
            (Loc.T("Copy address:port"), () => Copy(entry.Endpoint), true),
        };
        if (url is not null)
        {
            menu.Add((null, null, true));
            menu.Add((Loc.T("Open in browser"), () => UiKit.OpenUrl(url), true));
        }

        border.ContextMenu = V.Menu([.. menu]);
        return border;
    }

    private static void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private async void Terminate(PortEntry entry, bool force)
    {
        if (entry.StartedAt == 0 || entry.IsProtected)
        {
            return;
        }

        if (!Dialogs.Confirm(Loc.F("Terminate {0}?", entry.ProcessName), Loc.F("This closes port {0} by terminating PID {1}.", entry.Port, entry.Pid), force ? Loc.T("Force Kill") : Loc.T("Kill"), true, Loc.T("Cancel"), _keepOpen))
        {
            return;
        }

        await KillService.Shared.KillAsync([new KillTarget(entry.Pid, entry.StartedAt, entry.ProcessName)], force);
        await Task.Delay(400);
        await RefreshAsync();
    }
}

/// <summary>Port Manager: the catalog entry "port-manager". The UI lives in the contributions.</summary>
public sealed class PortManagerFeature : IFeature
{
    private IFeatureContext? _context;

    public string Id => "port-manager";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Port Manager"), () => _context?.Shell.OpenSettings(SettingsPages.PortManager), G.Network),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        Host.Attach(context);
    }

    public void Stop()
    {
        _context = null;
    }
}

/// <summary>Settings page "Port Manager".</summary>
public sealed class PortManagerSettingsSection : ISettingsSection
{
    public string? FeatureId => "port-manager";

    public string PageId => SettingsPages.PortManager;

    public string? Title => Loc.T("Port Manager");

    public IReadOnlyList<string> Keywords => ["port", "ports", "listening", "socket", "PID", "kill port"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        return new PortManagerView(false);
    }
}

/// <summary>Utilities row "Port Manager": the hosted panel view.</summary>
public sealed class PortManagerUtility : IPanelUtility
{
    public string? FeatureId => "port-manager";

    public int Order => 180;

    public string Id => "portManager";

    public string Title => Loc.T("Port Manager");

    public string Glyph => G.Network;

    public string? Caption => Loc.T("Your listening ports");

    public string? SettingsDestination => SettingsPages.PortManager;

    public bool ShowsSettingsGear => true;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new PortManagerView(true, c.RequestRelayout, c.SetKeepOpen);
    }
}
