// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core.Ui;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>
/// "Get notified when Claude asks for permission": the on/off setting for the island notice, how detection works, and the opt-in
/// Claude Code hook (shown exactly, backed up before it is written, removable).
/// </summary>
internal sealed class AgentAttentionSettings : StackPanel
{
    private readonly Trayloom.Core.IAppSettings _app;
    private readonly string _settingsPath;
    private readonly TextBlock _status;
    private readonly TextBlock _result;
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private readonly Border _preview;
    private bool _previewShown;

    public AgentAttentionSettings(Trayloom.Core.IAppSettings app)
    {
        _app = app;
        _settingsPath = AgentHooks.SettingsPath(AgentLogRoot.Home);
        _status = UiKit.Text("", 12.5, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true);
        _result = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);

        var toggle = new SettingsRow("", Loc.T("Tell me when an agent needs me"),
            app.Get(AgentKeys.IncludeClaudeCode, false)
                ? Loc.T("Shows “Claude needs your permission - project: tool” on the island, and an amber “Waiting for you” state in the Now card and the closed island. Applies to Claude Code (terminal and Claude app) and Codex.")
                : Loc.T("Shows “Claude needs your permission - project: tool” on the island, and an amber “Waiting for you” state in the Now card and the closed island. Applies to the Claude app’s Code tab and Codex."),
            new MacSwitch(app.Get(AgentKeys.Attention, true), SwitchSize.Regular, on => app.Set(AgentKeys.Attention, on)));
        Children.Add(toggle);

        var how = UiKit.Text(Loc.T("Without any change to Claude Code, Trayloom notices a tool call that has had no result for about 8 seconds while nothing is running for it and nothing is being written (20 seconds for web and MCP tools). That is shown as “probably a permission prompt”, because a slow tool looks the same. The hook below makes it exact and immediate."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        how.Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0);
        Children.Add(how);

        var group = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 12, 0, 0) };
        group.Children.Add(UiKit.Text(Loc.T("Reliable alerts (optional)"), 13, FontWeights.Medium, ThemeKeys.TextPrimary));
        _status.Margin = new Thickness(0, 4, 0, 0);
        group.Children.Add(_status);
        var what = UiKit.Text("Adds a Notification and a PermissionRequest hook to " + _settingsPath + ". Each runs a small script that saves the session id, the tool name and a short hint to a file in Trayloom's own folder; it prints nothing and never changes what Claude Code decides. Your other settings and hooks are kept, and a copy of settings.json is saved first. Running sessions pick it up when they restart.", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        what.Margin = new Thickness(0, 6, 0, 0);
        group.Children.Add(what);

        _buttons.Margin = new Thickness(0, 10, 0, 0);
        group.Children.Add(_buttons);
        _result.Margin = new Thickness(0, 6, 0, 0);
        group.Children.Add(_result);

        var showPreview = UiKit.Button(Loc.T("Show exactly what will be added"), ButtonKind.Plain, 11.5);
        showPreview.HorizontalAlignment = HorizontalAlignment.Left;
        showPreview.Margin = new Thickness(0, 6, 0, 0);
        _preview = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 0, 0) };
        _preview.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var code = UiKit.Text(AgentHooks.Preview() + "\n\nscript: " + AgentHooks.ScriptPath + "\nevents: " + AgentHooks.EventsFolder, 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        code.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _preview.Child = code;
        showPreview.Click += (_, _) =>
        {
            _previewShown = !_previewShown;
            _preview.Visibility = _previewShown ? Visibility.Visible : Visibility.Collapsed;
        };
        group.Children.Add(showPreview);
        group.Children.Add(_preview);
        Children.Add(group);
        Refresh();
    }

    private void Refresh()
    {
        var (installed, problem) = AgentHooks.Status(_settingsPath);
        _status.Text = problem ?? (installed ? Loc.T("On: Claude Code reports permission requests to Trayloom.") : Loc.T("Off: Trayloom relies on the automatic detection above."));
        _status.SetResourceReference(TextBlock.ForegroundProperty, problem is not null ? ThemeKeys.Orange : installed ? ThemeKeys.Green : ThemeKeys.TextPrimary);
        _buttons.Children.Clear();
        if (problem is null)
        {
            _buttons.Children.Add(installed
                ? UiKit.Button(Loc.T("Remove the hook"), ButtonKind.Bordered, 12, null, Remove)
                : UiKit.Button(Loc.T("Get notified when Claude asks for permission"), ButtonKind.Prominent, 12, null, Install));
        }
    }

    private void Install()
    {
        var r = AgentHooks.Install(_settingsPath);
        _result.Text = r.Message + (r.BackupPath is { } b ? " Backup: " + b : "");
        Refresh();
    }

    private void Remove()
    {
        var r = AgentHooks.Remove(_settingsPath);
        _result.Text = r.Message + (r.BackupPath is { } b ? " Backup: " + b : "");
        Refresh();
    }
}

/// <summary>"Where these numbers come from": every limit source with its raw values and when each was read.</summary>
internal sealed class LimitDiagnosticsView : StackPanel
{
    private readonly AgentsRuntime? _runtime;
    private readonly StackPanel _list = new();
    private readonly TextBlock _chevron;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _open;

    public LimitDiagnosticsView(AgentsRuntime? runtime, bool openByDefault = false)
    {
        _runtime = runtime;
        _open = openByDefault;
        var header = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(Loc.T("Where these numbers come from"), 13, FontWeights.Medium, ThemeKeys.TextPrimary));
        _chevron = UiKit.Icon(_open ? Glyphs.ChevronDownSmall : Glyphs.ChevronRight, 10, ThemeKeys.TextTertiary);
        Grid.SetColumn(_chevron, 1);
        header.Children.Add(_chevron);
        header.MouseLeftButtonUp += (_, _) =>
        {
            _open = !_open;
            _chevron.Text = _open ? Glyphs.ChevronDownSmall : Glyphs.ChevronRight;
            Build();
        };
        Children.Add(header);
        _list.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_list);
        Build();
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            _timer.Start();
            if (_runtime is not null)
            {
                _runtime.Service.Changed += OnChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            if (_runtime is not null)
            {
                _runtime.Service.Changed -= OnChanged;
            }
        };
    }

    private void OnTick(object? sender, EventArgs e) => Build();

    private void OnChanged() => Dispatcher.BeginInvoke(Build);

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("ddd MMM d, h:mm:ss tt", System.Globalization.CultureInfo.CurrentCulture);

    private void Build()
    {
        _list.Children.Clear();
        _list.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;
        if (!_open)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var sources = _runtime?.Service.Snapshot.LimitSources ?? [];
        var intro = UiKit.Text(Loc.T("Each source below is shown as it reads right now, so you can check it against Claude (Settings > Usage) and the Codex app. The newest reading is the one Trayloom shows; a source is never mixed with another. Reset times appear only when a source reports one."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        _list.Children.Add(intro);
        var plans = _runtime?.Service.Snapshot.Plans;
        if (plans is not null && plans.TryGetValue(AgentProvider.Claude, out var claudePlan) && AgentLimitPlan.Describe(AgentProvider.Claude, claudePlan) is { } planNote)
        {
            var plan = UiKit.Text(Loc.F("Your Claude plan: {0}. ", claudePlan.Name) + planNote, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            plan.Margin = new Thickness(0, 8, 0, 0);
            _list.Children.Add(plan);
        }

        if (sources.Count == 0)
        {
            var none = UiKit.Text(Loc.T("No readings yet. Open the AI Agents page in the island, or press Read now."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            none.Margin = new Thickness(0, 8, 0, 0);
            _list.Children.Add(none);
        }

        foreach (var s in sources)
        {
            _list.Children.Add(Block(s, now));
        }

        var button = UiKit.Button(Loc.T("Read now"), ButtonKind.Bordered, 12, Glyphs.Refresh, () =>
        {
            if (_runtime is null)
            {
                return;
            }

            _runtime.Service.PageDidAppear(true);
            _runtime.Service.CheckClaudeAccount();
            _runtime.Resets.Refresh();
        });
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 10, 0, 0);
        _list.Children.Add(button);
    }

    private static FrameworkElement Block(LimitSourceInfo s, DateTime now)
    {
        var box = new Border { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 8, 0, 0) };
        box.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var stack = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Text(s.Provider.DisplayName() + " · " + s.Name, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        if (s.InUse)
        {
            var pill = new BadgePill(Loc.T("In use"), BadgeStyle.Green) { Margin = new Thickness(8, 0, 0, 0) };
            title.Children.Add(pill);
        }

        stack.Children.Add(title);
        void Line(string label, string value, string key = ThemeKeys.TextSecondary)
        {
            var t = UiKit.Text(label + ": " + value, 11.5, FontWeights.Normal, key, wrap: true);
            t.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(t);
        }

        Line(Loc.T("Read from"), s.Where);
        Line("Observed", s.ObservedAt is { } at ? Local(at) + " (" + AgentFormat.Relative(at, now, false) + ")" : "never");
        Line(Loc.T("Status"), s.Status, s.InUse ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary);
        foreach (var raw in s.Raw)
        {
            var t = UiKit.Text(raw, 11, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
            t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            t.Margin = new Thickness(10, 2, 0, 0);
            stack.Children.Add(t);
        }

        box.Child = stack;
        return box;
    }
}
