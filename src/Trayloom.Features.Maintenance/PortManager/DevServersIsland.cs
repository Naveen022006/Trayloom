// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Maintenance;

/// <summary>Brushes and small builders for the dev servers page. The island is always dark, so everything is white at some opacity.</summary>
internal static class DevLook
{
    private static readonly Dictionary<(uint, int), SolidColorBrush> Cache = [];

    public static Brush Of(Color color, double opacity = 1)
    {
        var key = ((uint)(color.R << 16 | color.G << 8 | color.B), (int)Math.Round(opacity * 1000));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush White(double opacity) => Of(Colors.White, opacity);

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, bool wrap = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? White(1),
            FontFamily = UiKit.TextFont,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = UiKit.IconFont,
        FontSize = size,
        Foreground = brush ?? White(0.6),
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    /// <summary>A rounded pill button that UI Automation can find and invoke.</summary>
    public static Button Pill(string text, Action onClick, string automationId, Brush? fill = null, Brush? fillHover = null)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.PaddingProperty, new Thickness(11, 4, 11, 4));
        border.SetValue(Border.BackgroundProperty, fill ?? White(0.1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, fillHover ?? fill ?? White(0.2), "chrome"));
        template.Triggers.Add(hover);
        var button = new Button
        {
            Content = Text(text, 11.5, FontWeights.Medium),
            Template = template,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            FocusVisualStyle = null,
        };
        button.Click += (_, _) => onClick();
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, text);
        return button;
    }
}

/// <summary>
/// What outlives a rebuild of the dev servers page: the last list, the row waiting for its Stop confirmation, the result line, and the
/// hold that keeps the island open while a confirmation is up. The island rebuilds its pages whenever a song changes.
/// </summary>
internal static class DevServersState
{
    private static IDisposable? s_hold;
    private static int s_views;
    private static DispatcherTimer? s_timer;

    /// <summary>The last list that was read, so a rebuilt page does not flash empty.</summary>
    public static List<PortEntry>? Last { get; set; }

    /// <summary>The process (pid and creation time) whose Stop is waiting for the person to confirm, or null.</summary>
    public static (int Pid, long StartedAt)? Pending { get; set; }

    /// <summary>The inline result of the last Stop, shown for a few seconds.</summary>
    public static string? Result { get; set; }

    public static DateTime ResultUntil { get; set; }

    public static void SetResult(string text)
    {
        Result = text;
        ResultUntil = DateTime.UtcNow.AddSeconds(5);
    }

    /// <summary>A view of the page entered the tree: while a confirmation is pending the island is held open again.</summary>
    public static void Attach(IIslandContext context)
    {
        s_views++;
        s_timer?.Stop();
        if (Pending is not null)
        {
            s_hold ??= context.HoldOpen();
        }
    }

    /// <summary>
    /// A view left the tree. A rebuild puts a new one back within the same moment, so the hold is only let go when no view returns for
    /// a short while; otherwise the island would never collapse by itself again.
    /// </summary>
    public static void Detach()
    {
        s_views = Math.Max(0, s_views - 1);
        if (s_views > 0 || s_hold is null)
        {
            return;
        }

        if (s_timer is null)
        {
            s_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            s_timer.Tick += (_, _) =>
            {
                s_timer?.Stop();
                if (s_views == 0)
                {
                    Pending = null;
                    Release();
                }
            };
        }

        s_timer.Stop();
        s_timer.Start();
    }

    public static void Hold(IIslandContext context) => s_hold ??= context.HoldOpen();

    public static void Release()
    {
        s_hold?.Dispose();
        s_hold = null;
    }
}

/// <summary>The "Dev servers" page of the expanded island: local servers the person started, with Open and an inline-confirmed Stop.</summary>
public sealed class DevServersIslandSection : IIslandSection
{
    public string? FeatureId => "port-manager";

    public int Order => 130;

    public string Id => "devServers";

    public string Title => Loc.T("Dev servers");

    public string Glyph => G.Network;

    public FrameworkElement CreateView(IIslandContext context)
    {
        Host.Attach(context);
        return new DevServersView(context);
    }
}

/// <summary>
/// The page itself. It reads the port list every 3 s while it is on screen and never acts without the inline confirmation. Stop ends
/// the process the way Task Manager does; it never asks for administrator rights and never shows a dialog.
/// </summary>
internal sealed class DevServersView : Border, IIslandLayerHost
{
    private readonly IIslandContext _context;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _refreshing;
    private bool _failed;
    private double _lastHeight;
    private bool _relayoutPending;

    public DevServersView(IIslandContext context)
    {
        _context = context;
        Child = Build();
        _timer.Tick += (_, _) => OnTick();
        Loaded += (_, _) =>
        {
            DevServersState.Attach(_context);
            _timer.Start();
            RefreshSafe();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            DevServersState.Detach();
        };
    }

    /// <summary>Esc and Back close the Stop confirmation before they leave the page.</summary>
    public bool CanStepBack => DevServersState.Pending is not null;

    public bool StepBack()
    {
        if (DevServersState.Pending is null)
        {
            return false;
        }

        CancelPending();
        return true;
    }

    private void OnTick()
    {
        if (DevServersState.Result is not null && DateTime.UtcNow >= DevServersState.ResultUntil)
        {
            DevServersState.Result = null;
            Rebuild();
        }

        RefreshSafe();
    }

    private async void RefreshSafe()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Host.Log("Dev servers: refresh failed", ex);
        }
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
            var showAll = Host.App.Get<bool>("devServersShowAll");
            var list = await Task.Run(() =>
            {
                var all = PortList.Read();
                return all is null ? null : DevServerRules.Filter(all, Sessions.IsMine, showAll);
            }).ConfigureAwait(true);
            if (list is null)
            {
                // The last list stays, with a line saying it may be out of date.
                _failed = true;
            }
            else
            {
                _failed = false;
                DevServersState.Last = list;
                if (DevServersState.Pending is { } pending && !list.Any(e => e.Pid == pending.Pid && e.StartedAt == pending.StartedAt))
                {
                    // The server went away by itself while the question was open.
                    DevServersState.Pending = null;
                    DevServersState.Release();
                }
            }
        }
        finally
        {
            _refreshing = false;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Child = Build();
        Measure(new Size(Math.Max(1, ActualWidth), double.PositiveInfinity));
        if (Math.Abs(DesiredSize.Height - _lastHeight) > 0.5)
        {
            _lastHeight = DesiredSize.Height;
            Relayout();
        }
    }

    /// <summary>Asks the island to measure the page again, once per burst of changes.</summary>
    private void Relayout()
    {
        if (_relayoutPending)
        {
            return;
        }

        _relayoutPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _relayoutPending = false;
            _context.Relayout();
        });
    }

    private FrameworkElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (DevServersState.Result is { } result && DateTime.UtcNow < DevServersState.ResultUntil)
        {
            var line = DevLook.Text(result, 11.5, FontWeights.Medium, DevLook.White(0.9), wrap: true);
            line.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(line);
        }

        if (_failed)
        {
            var warning = DevLook.Text(Loc.T("Couldn’t read the port list."), 11, null, DevLook.Of(Color.FromRgb(0xFF, 0x9F, 0x0A)), wrap: true);
            warning.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(warning);
        }

        var list = DevServersState.Last;
        if (list is null)
        {
            // Nothing read yet: keep the page its usual height instead of flashing the empty state.
            root.Children.Add(new Border { Height = 56 });
        }
        else if (list.Count == 0)
        {
            root.Children.Add(Empty());
        }
        else
        {
            var rows = new StackPanel();
            foreach (var entry in list)
            {
                var row = Row(entry);
                row.Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 6, 0, 0);
                rows.Children.Add(row);
            }

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 230,
                Content = rows,
            });
        }

        var footer = DevLook.Pill(Loc.T("All ports…"), () => _context.Shell.OpenSettings(SettingsPages.PortManager), "DevServers.AllPorts");
        footer.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(footer);
        return root;
    }

    private static FrameworkElement Empty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 4) };
        var icon = DevLook.Icon(G.Network, 20, DevLook.White(0.5));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(icon);
        var title = DevLook.Text(Loc.T("No local servers are running."), 13, null, DevLook.White(0.9), wrap: true);
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(title);
        var caption = DevLook.Text(Loc.T("Servers you start, like npm run dev, appear here."), 11, null, DevLook.White(0.6), wrap: true);
        caption.TextAlignment = TextAlignment.Center;
        caption.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(caption);
        return panel;
    }

    private FrameworkElement Row(PortEntry entry)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Background = DevLook.White(0.07) };
        if (DevServersState.Pending is { } pending && pending.Pid == entry.Pid && pending.StartedAt == entry.StartedAt)
        {
            card.Child = Confirm(entry);
            return card;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(DevLook.Text("localhost:" + entry.Port, 12.5, FontWeights.SemiBold));
        text.Children.Add(DevLook.Text(entry.ProcessName, 10.5, null, DevLook.White(0.55)));
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var url = entry.BrowserUrl;
        var open = DevLook.Pill(Loc.T("Open"), () =>
        {
            if (url is not null)
            {
                UiKit.OpenWebUrl(url);
            }
        }, "DevServers.Open." + entry.Port);
        open.IsEnabled = url is not null;
        buttons.Children.Add(open);
        var stop = DevLook.Pill(Loc.T("Stop"), () => AskToStop(entry), "DevServers.Stop." + entry.Port);
        stop.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(stop);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        card.Child = grid;
        return card;
    }

    /// <summary>The row turned into its Stop question: what will happen, and Stop / Cancel.</summary>
    private FrameworkElement Confirm(PortEntry entry)
    {
        var panel = new StackPanel();
        panel.Children.Add(DevLook.Text(Loc.F("Stop {0} on port {1}?", entry.ProcessName, entry.Port), 12.5, FontWeights.SemiBold, null, wrap: true));
        var caption = DevLook.Text(Loc.T("It ends the process like Task Manager. Unsaved work in it is lost."), 11, null, DevLook.White(0.65), wrap: true);
        caption.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(caption);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var red = Color.FromRgb(0xFF, 0x45, 0x3A);
        buttons.Children.Add(DevLook.Pill(Loc.T("Stop"), () => StopSafe(entry), "DevServers.StopConfirm." + entry.Port, DevLook.Of(red, 0.85), DevLook.Of(red)));
        var cancel = DevLook.Pill(Loc.T("Cancel"), CancelPending, "DevServers.StopCancel." + entry.Port);
        cancel.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        return panel;
    }

    private void AskToStop(PortEntry entry)
    {
        // A row the list shows is stoppable by construction, but the entry is checked again here in case the list is older than a second.
        if (entry.StartedAt == 0 || entry.IsProtected)
        {
            DevServersState.SetResult(Loc.T("Run by Windows or another account; Trayloom won’t stop it."));
            Rebuild();
            return;
        }

        DevServersState.Pending = (entry.Pid, entry.StartedAt);
        DevServersState.Hold(_context);
        Rebuild();
    }

    private void CancelPending()
    {
        DevServersState.Pending = null;
        DevServersState.Release();
        Rebuild();
    }

    private async void StopSafe(PortEntry entry)
    {
        try
        {
            await StopAsync(entry).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Host.Log("Dev servers: stop failed", ex);
            DevServersState.SetResult(Loc.T("Couldn’t stop it. It may run as administrator."));
            Rebuild();
        }
    }

    private async Task StopAsync(PortEntry entry)
    {
        DevServersState.Pending = null;
        DevServersState.Release();

        // The pid may have been reused since the row was drawn, so identity is checked again from the creation time.
        if (entry.StartedAt == 0 || entry.IsProtected || !Sessions.IsMine(entry.Pid))
        {
            DevServersState.SetResult(Loc.T("Run by Windows or another account; Trayloom won’t stop it."));
            Rebuild();
            return;
        }

        string result;
        if (!KillService.IsSameProcess(entry.Pid, entry.StartedAt))
        {
            // The server this row stood for is already gone.
            result = Loc.T("Stopped");
        }
        else
        {
            // Not interactive: no administrator prompt and no dialog, so a process that needs elevation simply fails.
            var ok = await KillService.Shared.KillAsync([new KillTarget(entry.Pid, entry.StartedAt, entry.ProcessName)], false, interactive: false).ConfigureAwait(true);
            if (ok)
            {
                // A close request to a window is not an exit: wait briefly and report what really happened.
                await Task.Delay(600).ConfigureAwait(true);
                ok = !KillService.IsSameProcess(entry.Pid, entry.StartedAt);
            }

            result = ok ? Loc.T("Stopped") : Loc.T("Couldn’t stop it. It may run as administrator.");
            Host.Log(ok ? "Dev servers: stopped one" : "Dev servers: could not stop one");
        }

        DevServersState.SetResult(result);
        Rebuild();
        await RefreshAsync().ConfigureAwait(true);
    }
}

/// <summary>Queue of "{0} is up" notices between the watcher (any thread) and the strip (UI thread).</summary>
internal static class DevServerNotices
{
    private static readonly object Gate = new();
    private static readonly Queue<(string Text, DateTime At)> Pending = new();

    /// <summary>The island context captured by the strip, so a new notice can ask the island to redraw.</summary>
    public static IIslandContext? Context { get; set; }

    public static void Add(string text)
    {
        lock (Gate)
        {
            while (Pending.Count >= 3)
            {
                Pending.Dequeue();
            }

            Pending.Enqueue((text, DateTime.UtcNow));
        }
    }

    /// <summary>The next notice that is still fresh, or null. A notice older than a minute is dropped: it would be news to nobody.</summary>
    public static string? Take()
    {
        lock (Gate)
        {
            while (Pending.Count > 0)
            {
                var (text, at) = Pending.Dequeue();
                if (DateTime.UtcNow - at < TimeSpan.FromMinutes(1))
                {
                    return text;
                }
            }

            return null;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Pending.Clear();
        }
    }
}

/// <summary>The "devServers.notice" strip: "localhost:5173 is up", shown once on the island when a new local server started.</summary>
public sealed class DevServersNoticeStrip : IIslandStrip
{
    public string? FeatureId => "port-manager";

    public int Order => 47;

    public string Id => "devServers.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        // Captured early so the watcher can ask the island to redraw when a server appears.
        DevServerNotices.Context = context;
        if (DevServerNotices.Take() is not { } text)
        {
            return null;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = DevLook.Icon(G.Network, 12, DevLook.Of(Color.FromRgb(0x30, 0xD1, 0x58)));
        icon.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(icon);
        var label = DevLook.Text(text, 11, FontWeights.Medium, DevLook.White(0.95));
        label.MaxWidth = 260;
        row.Children.Add(label);
        return row;
    }
}
