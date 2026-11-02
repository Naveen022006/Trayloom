// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>DISKS tab (spec 01 5.6): disk selector, then usage, live activity, SMART, external protection and tools for the selected disk.</summary>
public sealed class DiskPanelSection : IPanelSection
{
    public string? FeatureId => "monitor-disk";

    public string TabId => PanelTabs.Disk;

    public bool SupportsEditing => true;

    public FrameworkElement CreateView(IPanelContext c)
    {
        SystemDefaults.Register(c.App);
        DetailRouter.Consume(c, PanelTabs.Disk);
        return DiskBlocks.Create(c);
    }
}

internal static class DiskBlocks
{
    private static string? _selected;
    private static readonly Dictionary<string, string> EjectState = new(StringComparer.OrdinalIgnoreCase);

    public static FrameworkElement Create(IPanelContext c)
    {
        var app = c.App;
        var card = new PanelCard(null, 10);
        var stack = new StackPanel();
        card.Child = stack;
        var selectorHost = new ContentControl();
        var blocksHost = new ContentControl { Margin = new Thickness(0, 10, 0, 0) };
        var empty = UiKit.Text(Strings.Monitor.NoDisks, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        stack.Children.Add(selectorHost);
        stack.Children.Add(blocksHost);
        IReadOnlyList<VolumeInfo> volumes = [];
        var signature = string.Empty;
        string? renderedFor = null;

        VolumeInfo? Current() => volumes.FirstOrDefault(v => v.Letter == _selected) ?? volumes.FirstOrDefault();

        void RenderBlocks()
        {
            var volume = Current();
            if (volume is null)
            {
                return;
            }

            var blocks = new List<BlockCard.Block>
            {
                new("usage", "monitorDiskUsage", () => Usage(c, volume.Letter)),
                new("activity", "monitorDiskActivity", () => Activity(c, volume.Letter)),
                new("smart", "monitorDiskSMART", () => Smart(c, volume.Letter)),
                new("protection", "monitorDiskProtection", () => Protection(c, volume.Letter, () => volumes), volume.External),
                new("tools", "monitorDiskTools", () => Tools(c, volume.Letter)),
            };
            blocksHost.Content = BlockCard.Create(c, "panelDiskOrder", "usage,activity,smart,protection,tools", blocks, null, bare: true);
        }

        void RenderSelector()
        {
            if (volumes.Count == 0)
            {
                selectorHost.Content = empty;
                blocksHost.Content = null;
                return;
            }

            var panel = new StackPanel();
            panel.Children.Add(UiKit.Text(Strings.Monitor.SelectDisk, 10, FontWeights.Medium, ThemeKeys.TextTertiary));
            var grid = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var current = Current();
            foreach (var v in volumes)
            {
                var button = SelectorButton(c, v, v.Letter == current?.Letter, () =>
                {
                    _selected = v.Letter;
                    signature = string.Empty;
                    Apply(volumes, force: true);
                });
                grid.Children.Add(button);
            }

            panel.Children.Add(grid);
            selectorHost.Content = panel;
        }

        void Apply(IReadOnlyList<VolumeInfo> list, bool force)
        {
            volumes = list;
            var sig = string.Join('|', list.Select(v => v.Letter + ":" + (int)(v.UsedFraction * 100) + EjectState.GetValueOrDefault(v.Letter)));
            if (sig == signature && !force)
            {
                return;
            }

            signature = sig;
            RenderSelector();
            var letter = Current()?.Letter;
            if (blocksHost.Content is null || force || renderedFor != letter)
            {
                RenderBlocks();
                renderedFor = letter;
            }

            c.RequestRelayout();
        }

        var live = new LiveHost(card, Needs.DiskSpace);
        live.Add(s => Apply(s.Volumes, force: false));
        if (MonitorService.Last?.Volumes is { Count: > 0 } known)
        {
            Apply(known, force: true);
        }
        else
        {
            selectorHost.Content = empty;
        }

        return card;
    }

    private static FrameworkElement SelectorButton(IPanelContext c, VolumeInfo v, bool selected, Action select)
    {
        var grid = new Grid { MinWidth = 96, MaxWidth = 138 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Icon(v.External ? Gl.Usb : Gl.Disk, 10, ThemeKeys.TextPrimary));
        var name = UiKit.Text(v.DisplayName, 10.5, FontWeights.SemiBold, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(4, 0, 0, 0);
        name.MaxWidth = 70;
        head.Children.Add(name);
        text.Children.Add(head);
        text.Children.Add(UiKit.Text(Loc.F("{0}% used", Math.Round(v.UsedFraction * 100)), 9, FontWeights.Normal, ThemeKeys.TextSecondary));
        grid.Children.Add(text);
        if (v.External)
        {
            var state = EjectState.GetValueOrDefault(v.Letter);
            var glyph = state switch { "ejecting" => "", "ready" => Gl.CheckCircle, "failed" => Gl.Eject, _ => Gl.Eject };
            var tip = state switch { "ejecting" => Strings.Monitor.Ejecting, "ready" => Strings.Monitor.ReadyToRemove, "failed" => Strings.Monitor.EjectFailed, _ => Strings.Monitor.Eject };
            var color = state switch { "ready" => ThemeKeys.Green, "failed" => ThemeKeys.Red, _ => ThemeKeys.TextSecondary };
            var eject = Bits.FlatIcon(glyph, tip, () => EjectVolume(v.Letter), 9, 18, color);
            Grid.SetColumn(eject, 1);
            grid.Children.Add(eject);
        }

        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 5, 5, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Child = grid, Background = Brushes.Transparent };
        border.SetResourceReference(Border.BackgroundProperty, selected ? ThemeKeys.AccentFill18 : ThemeKeys.ControlFill);
        border.MouseLeftButtonUp += (_, _) => select();
        return border;
    }

    private static void EjectVolume(string letter)
    {
        EjectState[letter] = "ejecting";
        Task.Run(() => StorageReader.Eject(letter)).ContinueWith(t =>
        {
            EjectState[letter] = t.IsCompletedSuccessfully && t.Result ? "ready" : "failed";
            Application.Current?.Dispatcher.BeginInvoke(() => MonitorService.RequestNow());
        });
    }

    private static TextBlock Header(string text) => UiKit.Text(text, 10, FontWeights.Medium, ThemeKeys.TextTertiary);

    private static VolumeInfo? Find(string letter) => MonitorService.Last?.Volumes.FirstOrDefault(v => v.Letter == letter);

    private static FrameworkElement TitleRow(VolumeInfo? v)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Icon(v?.External == true ? Gl.Usb : Gl.Disk, 10, ThemeKeys.TextPrimary));
        var name = UiKit.Text(v?.DisplayName ?? "-", 11.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        name.Margin = new Thickness(5, 0, 0, 0);
        title.Children.Add(name);
        row.Children.Add(title);
        var tags = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var tag in new[] { v?.FileSystem, v?.External == true ? "External" : "Internal" })
        {
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            var t = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 0, 0, 0), Child = UiKit.Text(tag, 9, FontWeights.Medium, ThemeKeys.TextSecondary) };
            t.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            tags.Children.Add(t);
        }

        Grid.SetColumn(tags, 2);
        row.Children.Add(tags);
        return row;
    }

    private static FrameworkElement? Usage(IPanelContext c, string letter)
    {
        var stack = new StackPanel();
        stack.Children.Add(Header(Strings.Monitor.DiskUsage));
        var volume = Find(letter);
        var title = TitleRow(volume);
        title.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(title);
        var bar = new UsageBar(volume?.UsedFraction ?? 0, ThemeKeys.Accent, 6) { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(bar);
        var line = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var used = UiKit.Text("-", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        used.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        var avail = UiKit.Text("-", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        avail.FontFamily = used.FontFamily;
        Grid.SetColumn(avail, 1);
        line.Children.Add(used);
        line.Children.Add(avail);
        stack.Children.Add(line);
        var totalLine = UiKit.Text("-", 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        totalLine.FontFamily = used.FontFamily;
        totalLine.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(totalLine);

        void Update(MonitorSnapshot s)
        {
            var v = s.Volumes.FirstOrDefault(x => x.Letter == letter);
            if (v is null)
            {
                return;
            }

            var fraction = v.UsedFraction;
            bar.Value = fraction;
            bar.FillKey = fraction < 0.75 ? ThemeKeys.Accent : fraction < 0.9 ? ThemeKeys.Yellow : ThemeKeys.Red;
            used.Text = Loc.F("{0}% used", Math.Round(fraction * 100));
            avail.Text = Loc.F("{0} available", Fmt.BytesDecimal(v.Free));
            totalLine.Text = $"{Fmt.BytesDecimal(v.Total - v.Free)} / {Fmt.BytesDecimal(v.Total)}";
        }

        var live = new LiveHost(stack, Needs.DiskSpace);
        live.Add(Update);
        if (MonitorService.Last is { } last)
        {
            Update(last);
        }

        return stack;
    }

    private static FrameworkElement? Activity(IPanelContext c, string letter)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(Header(Strings.Monitor.LiveActivity));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        (StackPanel, TextBlock) Col(string glyph, string key, string label)
        {
            var col = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(UiKit.Icon(glyph, 12, key, FontWeights.SemiBold));
            var value = UiKit.Text("-", 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            value.FontFamily = UiKit.DisplayFont;
            value.Margin = new Thickness(4, 0, 0, 0);
            top.Children.Add(value);
            col.Children.Add(top);
            col.Children.Add(UiKit.Text(label, 10, FontWeights.Normal, ThemeKeys.TextSecondary));
            return (col, value);
        }

        var (readCol, readValue) = Col(Gl.Down, ThemeKeys.Accent, Strings.Monitor.Read);
        var (writeCol, writeValue) = Col(Gl.Up, ThemeKeys.Pink, Strings.Monitor.Write);
        row.Children.Add(readCol);
        Grid.SetColumn(writeCol, 1);
        row.Children.Add(writeCol);
        stack.Children.Add(row);
        var graph = new GraphView(app, ThemeKeys.Accent, 30, ThemeKeys.Pink) { Margin = new Thickness(0, 8, 0, 0) };
        var showGraph = app.Get<bool>("monitorGraphDisk");
        if (showGraph)
        {
            stack.Children.Add(graph);
        }

        var session = UiKit.Text(string.Empty, 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        session.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(session);
        double sessionRead = 0;
        double sessionWrite = 0;
        var lastAt = DateTime.MinValue;
        var live = new LiveHost(stack, Needs.Disk);
        live.Add(s =>
        {
            var (r, w) = s.DiskByLetter.TryGetValue(letter, out var pair) ? pair : (s.DiskRead, s.DiskWrite);
            readValue.Text = Fmt.Rate(r, false);
            writeValue.Text = Fmt.Rate(w, false);
            if (lastAt != DateTime.MinValue)
            {
                var dt = (s.At - lastAt).TotalSeconds;
                sessionRead += r * dt;
                sessionWrite += w * dt;
            }

            lastAt = s.At;
            session.Text = $"{Strings.Monitor.ThisSession}  ↓{Fmt.BytesDecimal(sessionRead)}  ↑{Fmt.BytesDecimal(sessionWrite)}";
            if (showGraph)
            {
                var rs = MonitorService.Series("diskR");
                var ws = MonitorService.Series("diskW");
                var peak = Math.Max(rs.DefaultIfEmpty(0).Max(), ws.DefaultIfEmpty(0).Max());
                var ceiling = GraphView.RoundCeiling(Math.Max(peak, 1), 1);
                graph.Set(rs, ceiling, Fmt.Rate(ceiling, false), ws);
            }
        });
        return stack;
    }

    private static FrameworkElement? Smart(IPanelContext c, string letter)
    {
        var stack = new StackPanel();
        stack.Children.Add(Header(Strings.Monitor.Smart));
        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(body);

        void Update(MonitorSnapshot s)
        {
            body.Children.Clear();
            if (!s.Smart.TryGetValue(letter, out var info))
            {
                body.Children.Add(UiKit.Text(Strings.Monitor.SmartUnavailable, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                return;
            }

            foreach (var (label, value) in new[]
            {
                (Loc.T("Status"), info.Status), (Loc.T("Total written"), info.TotalWritten), (Loc.T("Total read"), info.TotalRead), (Loc.T("Temperature"), info.Temperature),
                (Loc.T("Health"), info.Health), (Loc.T("Power cycles"), info.PowerCycles), (Loc.T("Power on hours"), info.PowerOnHours),
            })
            {
                if (value is null)
                {
                    continue;
                }

                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(UiKit.Text(label, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                var v = UiKit.Text(value, 10.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
                v.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                Grid.SetColumn(v, 1);
                row.Children.Add(v);
                body.Children.Add(row);
            }
        }

        var live = new LiveHost(stack, Needs.Smart | Needs.DiskSpace);
        live.Add(Update);
        if (MonitorService.Last is { } last)
        {
            Update(last);
        }
        else
        {
            body.Children.Add(UiKit.Text(Strings.Monitor.SmartUnavailable, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        }

        return stack;
    }

    private static FrameworkElement? Protection(IPanelContext c, string letter, Func<IReadOnlyList<VolumeInfo>> volumes)
    {
        var stack = new StackPanel();
        stack.Children.Add(Header(Strings.Monitor.ExternalProtection));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var eject = PanelButtons.Mini(Strings.Monitor.Eject, Gl.Eject, () => EjectVolume(letter));
        var all = PanelButtons.Mini(Strings.Monitor.EjectAll, Gl.Eject, () =>
        {
            var excluded = c.App.Get<List<string>>("diskEjectExcludedVolumes");
            foreach (var v in volumes().Where(v => v.External && !IsExcluded(excluded, v)))
            {
                EjectVolume(v.Letter);
            }
        });
        all.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(eject);
        row.Children.Add(all);
        stack.Children.Add(row);
        var anyExternal = volumes().Any(v => v.External);
        var state = EjectState.GetValueOrDefault(letter);
        var caption = UiKit.Text(state switch { "ready" => Strings.Monitor.ReadyToRemove, "failed" => Strings.Monitor.EjectFailed, "ejecting" => Strings.Monitor.Ejecting, _ => anyExternal ? Strings.Monitor.EjectBefore : Strings.Monitor.NoExternal }, 9.5, FontWeights.Normal, state switch { "ready" => ThemeKeys.Green, "failed" => ThemeKeys.Red, _ => ThemeKeys.TextSecondary }, wrap: true);
        caption.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(caption);
        return stack;
    }

    /// <summary>True when a volume is in the "Excluded drives" list (by name, drive letter or label, case-insensitive).</summary>
    public static bool IsExcluded(List<string> excluded, VolumeInfo v) =>
        excluded.Any(e => string.Equals(e, v.Label, StringComparison.OrdinalIgnoreCase) || string.Equals(e.TrimEnd('\\'), v.Letter, StringComparison.OrdinalIgnoreCase) || string.Equals(e, v.DisplayName, StringComparison.OrdinalIgnoreCase));

    private static FrameworkElement? Tools(IPanelContext c, string letter)
    {
        var stack = new StackPanel();
        stack.Children.Add(Header(Strings.Monitor.Tools));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = UiKit.Text(Find(letter)?.DisplayName ?? letter, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(name);
        var open = PanelButtons.Mini(Strings.Monitor.Open, Gl.Folder, () => Launch("explorer.exe", letter + "\\"));
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        var storage = PanelButtons.Mini(Strings.Monitor.Storage, Gl.Gear, () => Launch("ms-settings:storagesense", null));
        storage.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(storage, 2);
        row.Children.Add(storage);
        stack.Children.Add(row);
        return stack;
    }

    private static void Launch(string file, string? args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args ?? string.Empty) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
}
