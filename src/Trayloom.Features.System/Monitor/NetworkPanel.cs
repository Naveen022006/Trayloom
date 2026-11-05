// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>The result of a speed test (megabits per second and milliseconds).</summary>
internal sealed record SpeedResult(double DownloadMbps, double UploadMbps, double LatencyMs);

/// <summary>Latency x5 (best), 5 s download and 5 s upload against Cloudflare's speed endpoints. Only runs when the user asks for it.</summary>
internal static class SpeedTest
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// True when the current connection is metered, roaming or over its data limit, where a test of several hundred megabytes can
    /// cost money. False when Windows cannot tell.
    /// </summary>
    public static bool IsMetered()
    {
        try
        {
            var cost = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            if (cost is null)
            {
                return false;
            }

            return cost.NetworkCostType is Windows.Networking.Connectivity.NetworkCostType.Fixed or Windows.Networking.Connectivity.NetworkCostType.Variable
                   || cost.Roaming
                   || cost.OverDataLimit;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<SpeedResult?> RunAsync(CancellationToken cancel)
    {
        try
        {
            var best = double.MaxValue;
            for (var i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                using var response = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            // Download: read for five seconds.
            long bytes = 0;
            var clock = Stopwatch.StartNew();
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=90000000"))
            using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancel);
                var buffer = new byte[64 * 1024];
                while (clock.Elapsed < TimeSpan.FromSeconds(5))
                {
                    var read = await stream.ReadAsync(buffer, cancel);
                    if (read == 0)
                    {
                        break;
                    }

                    bytes += read;
                }
            }

            var download = bytes * 8 / 1_000_000.0 / Math.Max(0.1, clock.Elapsed.TotalSeconds);

            // Upload: post zero-filled bodies for five seconds.
            long sent = 0;
            clock.Restart();
            var payload = new byte[4 * 1024 * 1024];
            while (clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                using var content = new ByteArrayContent(payload);
                using var response = await Http.PostAsync("https://speed.cloudflare.com/__up", content, cancel);
                response.EnsureSuccessStatusCode();
                sent += payload.Length;
            }

            var upload = sent * 8 / 1_000_000.0 / Math.Max(0.1, clock.Elapsed.TotalSeconds);
            return new SpeedResult(download, upload, best);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>NETWORK tab (spec 01 5.5): live speed with unit toggle and graph, apps, session totals, addresses and a speed test.</summary>
public sealed class NetworkPanelSection : IPanelSection
{
    public string? FeatureId => "monitor-network";

    public string TabId => PanelTabs.Network;

    public bool SupportsEditing => true;

    public FrameworkElement CreateView(IPanelContext c)
    {
        SystemDefaults.Register(c.App);
        DetailRouter.Consume(c, PanelTabs.Network);
        var blocks = new List<BlockCard.Block>
        {
            new("speed", "monitorNetSpeed", () => NetworkBlocks.Speed(c)),
            new("apps", "monitorNetApps", () => NetworkBlocks.Apps(c)),
            new("totals", "monitorNetTotals", () => NetworkBlocks.Totals(c)),
            new("addresses", "monitorNetAddresses", () => NetworkBlocks.Addresses(c)),
            new("test", "monitorNetTest", () => NetworkBlocks.Test(c)),
        };
        return BlockCard.Create(c, "panelNetworkOrder", "speed,apps,totals,addresses,test", blocks);
    }
}

internal static class NetworkBlocks
{
    private static bool Bits_(IAppSettings app) => app.Get<string>("networkSpeedUnit") == "bits";

    public static FrameworkElement? Speed(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        (StackPanel Col, TextBlock Value) Column(string glyph, string key, string title)
        {
            var col = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(UiKit.Icon(glyph, 12, key, FontWeights.SemiBold));
            var value = UiKit.Text(Strings.Monitor.Measuring, 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            value.FontFamily = UiKit.DisplayFont;
            value.Margin = new Thickness(4, 0, 0, 0);
            top.Children.Add(value);
            col.Children.Add(top);
            col.Children.Add(UiKit.Text(title, 10, FontWeights.Normal, ThemeKeys.TextSecondary));
            return (col, value);
        }

        var (down, downValue) = Column(Gl.Down, ThemeKeys.Accent, Strings.Monitor.Download);
        var (up, upValue) = Column(Gl.Up, ThemeKeys.Green, Strings.Monitor.Upload);
        row.Children.Add(down);
        var divider = new Border { Width = 1, Height = 28 };
        divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
        Grid.SetColumn(divider, 2);
        row.Children.Add(divider);
        Grid.SetColumn(up, 4);
        row.Children.Add(up);

        var graph = new GraphView(app, ThemeKeys.Accent, 30, ThemeKeys.Green) { Margin = new Thickness(0, 8, 0, 0) };
        var showGraph = app.Get<bool>("monitorGraphNetwork");
        var unit = UiKit.Text(Bits_(app) ? "bit/s" : "B/s", 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        unit.FontFamily = UiKit.DisplayFont;
        unit.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border { Height = 22, CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 0, 7, 0), Cursor = Cursors.Hand, Child = unit, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Network speed unit · B/s → bit/s") };
        pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        Grid.SetColumn(pill, 5);
        row.Children.Add(pill);
        pill.MouseLeftButtonUp += (_, _) =>
        {
            app.Set("networkSpeedUnit", Bits_(app) ? "bytes" : "bits");
            unit.Text = Bits_(app) ? "bit/s" : "B/s";
            if (MonitorService.Last is { } last)
            {
                Update(last);
            }
        };
        stack.Children.Add(row);
        if (showGraph)
        {
            stack.Children.Add(graph);
        }

        void Update(MonitorSnapshot s)
        {
            var bits = Bits_(app);
            if (!s.NetKnown)
            {
                return;
            }

            downValue.Text = Fmt.Rate(s.NetDown, bits);
            upValue.Text = Fmt.Rate(s.NetUp, bits);
            if (showGraph)
            {
                var downSeries = MonitorService.Series("down");
                var upSeries = MonitorService.Series("up");
                var peak = Math.Max(downSeries.DefaultIfEmpty(0).Max(), upSeries.DefaultIfEmpty(0).Max());
                var unitSize = bits ? 1000.0 / 8 : 1024.0;
                var ceiling = GraphView.RoundCeiling(Math.Max(peak, 1), 1);
                // Round in the displayed unit (powers of 1024 for bytes, 1000 for bits).
                var scaled = bits ? peak * 8 : peak;
                var power = Math.Max(0, (int)Math.Floor(Math.Log(Math.Max(scaled, 1), bits ? 1000 : 1024)));
                var u = Math.Pow(bits ? 1000 : 1024, power);
                var roundedDisplay = GraphView.RoundCeiling(scaled / u, 1) * u;
                ceiling = bits ? roundedDisplay / 8 : roundedDisplay;
                _ = unitSize;
                graph.Set(downSeries, ceiling, Fmt.Rate(ceiling, bits), upSeries);
            }
        }

        var live = new LiveHost(stack, Needs.Network);
        live.Add(Update);
        return stack;
    }

    public static FrameworkElement? Apps(IPanelContext c)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(Strings.Monitor.AppsUsingNetwork, 10, FontWeights.Medium, ThemeKeys.TextTertiary));
        var list = new ProcessList(UsageKind.Cpu, Strings.Monitor.NoNetworkApps, 0, networkNotice: true) { Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(list);
        return stack;
    }

    public static FrameworkElement? Totals(IPanelContext c)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(Strings.Monitor.ThisSession, 10, FontWeights.Normal, ThemeKeys.TextTertiary));
        var value = UiKit.Text("↓-  ↑-", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        value.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        var live = new LiveHost(row, Needs.Network);
        live.Add(s => value.Text = $"↓{Fmt.BytesBinary(s.NetTotalDown)}  ↑{Fmt.BytesBinary(s.NetTotalUp)}");
        return row;
    }

    public static FrameworkElement? Addresses(IPanelContext c)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UiKit.Text(Strings.Monitor.LocalIPv4, 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        title.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(title);
        var lines = new StackPanel();
        Grid.SetColumn(lines, 1);
        row.Children.Add(lines);
        foreach (var (_, address) in SystemSampler.LocalAddresses())
        {
            var text = new TextBox { Text = address, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 10.5, FontWeight = FontWeights.Medium, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Right };
            text.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextSecondary);
            lines.Children.Add(text);
        }

        if (lines.Children.Count == 0)
        {
            lines.Children.Add(UiKit.Text("-", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary));
        }

        return row;
    }

    public static FrameworkElement? Test(IPanelContext c)
    {
        var stack = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var buttonHost = new ContentControl();
        row.Children.Add(buttonHost);
        var result = UiKit.Text(string.Empty, 11, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        result.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        result.HorizontalAlignment = HorizontalAlignment.Right;
        result.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(result, 1);
        row.Children.Add(result);
        stack.Children.Add(row);
        var latency = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        latency.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(latency);
        var ran = false;
        CancellationTokenSource? cts = null;

        void Idle()
        {
            var button = UiKit.Button(ran ? Strings.Monitor.TestAgain : Strings.Monitor.SpeedTest, ButtonKind.Bordered, 11, Gl.Gauge, Start);
            button.Padding = new Thickness(8, 3, 8, 3);
            buttonHost.Content = button;
        }

        async void Start()
        {
            if (SpeedTest.IsMetered()
                && MessageBox.Show(Loc.T("The speed test can use several hundred megabytes. Run it anyway?"), Loc.T("This connection is metered"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            ran = true;
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            var busy = new StackPanel { Orientation = Orientation.Horizontal };
            busy.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 40, Height = 4, VerticalAlignment = VerticalAlignment.Center });
            var t = UiKit.Text(Strings.Monitor.Testing, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            t.Margin = new Thickness(8, 0, 0, 0);
            busy.Children.Add(t);
            buttonHost.Content = busy;
            result.Text = string.Empty;
            latency.Text = string.Empty;
            var r = await SpeedTest.RunAsync(token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Idle();
            if (r is null)
            {
                latency.Text = Strings.Monitor.TestFailed;
                latency.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
                return;
            }

            string F(double v) => v >= 100 ? v.ToString("0", CultureInfo.CurrentCulture) : v.ToString("0.0", CultureInfo.CurrentCulture);
            result.Text = $"↓{F(r.DownloadMbps)} ↑{F(r.UploadMbps)} Mbps";
            latency.Text = Loc.F("Latency: {0} ms", Loc.Fmt(r.LatencyMs, "0"));
            latency.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary);
        }

        Idle();
        stack.Unloaded += (_, _) => cts?.Cancel();
        return stack;
    }
}
