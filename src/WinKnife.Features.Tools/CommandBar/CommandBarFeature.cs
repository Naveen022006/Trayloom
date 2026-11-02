// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>A searchable launcher for every feature action, installed applications, calculations and web searches.</summary>
public sealed class CommandBarFeature : ToolFeatureBase
{
    private readonly AppIndex _apps = new();
    private CommandBarWindow? _window;
    private Dictionary<string, int> _usage = new();

    public override bool InstalledByDefault => true;

    public override string Id => "command-bar";

    public override string Name => "Command bar";

    public override string Description => "Press a shortcut and type to run any WinKnife action, open an app, calculate or search the web.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Open the command bar", () => Safe(Show), ""),
    ];

    protected override void OnStart()
    {
        _usage = Ctx.Settings.Get("usage", new Dictionary<string, int>());
        _apps.RescanAsync();
        BindHotkey("hotkey", Hotkey.Parse("Ctrl+Alt+R"), Show);
    }

    protected override void OnStop()
    {
        _window?.Shutdown();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var rescan = UiKit.Btn("Rescan installed apps", () =>
        {
            _apps.RescanAsync();
            Ctx.Notifier.Show(Name, "Rescanning the Start Menu in the background.");
        });
        rescan.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open the command bar", Hotkey.Parse("Ctrl+Alt+R"), "Alt+Space is reserved by Windows (window menu); Ctrl+Alt+Space is taken by some other apps.")
            .AddToggle("includeApps", "Include installed apps", true, "Searches Start Menu shortcuts in addition to WinKnife actions.")
            .AddToggle("includeCalculator", "Calculate expressions", true, "Type something like 12*(3+4) and press Enter to copy the result.")
            .AddText("webSearchUrl", "Web search address", "https://www.google.com/search?q={0}", "Used by the 'Search the web' row. {0} is replaced with your text.");
        return UiKit.WithExtras(builder.Build(), rescan);
    }

    private void Show()
    {
        if (!IsRunning)
        {
            return;
        }

        if (DateTime.UtcNow - _apps.LastScan > TimeSpan.FromMinutes(10))
        {
            _apps.RescanAsync();
        }

        _window ??= new CommandBarWindow(Search, _apps.IconFor, Execute);
        _window.Present();
    }

    private IReadOnlyList<BarItem> Search(string query)
    {
        var items = new List<(BarItem Item, int Score)>();

        foreach (var (feature, action) in Ctx.Actions.All)
        {
            if (feature.Id == Id)
            {
                continue;
            }

            var key = $"{feature.Id}/{action.Id}";
            var item = new BarItem(key, action.Title, feature.Name, "Action", action.Glyph, null, () => action.Invoke());
            var score = Fuzzy.ScoreItem(query, action.Title, feature.Name);
            if (score is { } s)
            {
                items.Add((item, s + 30 + UsageBonus(key)));
            }
        }

        if (Ctx.Settings.Get("includeApps", true))
        {
            foreach (var app in _apps.Apps)
            {
                var key = "app/" + app.Name;
                var score = Fuzzy.ScoreItem(query, app.Name, null);
                if (score is { } s)
                {
                    var path = app.Path;
                    items.Add((new BarItem(key, app.Name, "Application", "App", "", path, () => LaunchApp(path)), s + UsageBonus(key)));
                }
            }
        }

        var ordered = query.Length == 0
            ? items.OrderByDescending(i => UsageBonus(i.Item.Key)).ThenBy(i => i.Item.Kind == "Action" ? 0 : 1).ThenBy(i => i.Item.Title, StringComparer.CurrentCultureIgnoreCase).Take(MaxEmptyRows(items.Count))
            : items.OrderByDescending(i => i.Score).ThenBy(i => i.Item.Title.Length).Take(24);

        var result = new List<BarItem>();
        if (query.Length > 0)
        {
            if (Ctx.Settings.Get("includeCalculator", true) && Calculator.TryEvaluate(query) is { } answer)
            {
                result.Add(new BarItem("calc", "= " + answer, "Press Enter to copy the result", "Calculator", "", null, () =>
                {
                    Clipboard.SetText(answer);
                    Ctx.Notifier.Show(Name, $"Copied {answer}");
                }));
            }

            var direct = DirectTarget(query);
            if (direct is not null)
            {
                result.Add(direct);
            }
        }

        result.AddRange(ordered.Select(i => i.Item));

        if (query.Length > 0)
        {
            var template = Ctx.Settings.Get("webSearchUrl", "https://www.google.com/search?q={0}");
            result.Add(new BarItem("web", $"Search the web for \"{query}\"", "Opens your default browser", "Web", "", null, () =>
            {
                var url = template.Replace("{0}", Uri.EscapeDataString(query));
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }));
        }

        return result;
    }

    private static int MaxEmptyRows(int total) => Math.Min(total, 12);

    private BarItem? DirectTarget(string query)
    {
        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return new BarItem("open:" + query, "Open " + query, "Opens in your default browser", "Link", "", null, () => Process.Start(new ProcessStartInfo(query) { UseShellExecute = true }));
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(query.Trim('"'));
            if (expanded.Length > 2 && (Directory.Exists(expanded) || File.Exists(expanded)))
            {
                return new BarItem("open:" + expanded, "Open " + expanded, "Opens with the default app", "Path", "", null, () => Process.Start(new ProcessStartInfo(expanded) { UseShellExecute = true }));
            }
        }
        catch
        {
            // not a path
        }

        return null;
    }

    private int UsageBonus(string key) => _usage.TryGetValue(key, out var count) ? Math.Min(count, 20) * 6 : 0;

    private void Execute(BarItem item)
    {
        if (item.Key is not ("calc" or "web") && !item.Key.StartsWith("open:", StringComparison.Ordinal))
        {
            _usage[item.Key] = _usage.GetValueOrDefault(item.Key) + 1;
            Ctx.Settings.Set("usage", _usage);
        }

        // Let the bar disappear and focus return to the previous window before the action runs
        // (important for screenshots, colour picking and anything that acts on the foreground window).
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Ctx.Ui) { Interval = TimeSpan.FromMilliseconds(160) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Safe(item.Run);
        };
        timer.Start();
    }

    private void LaunchApp(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
