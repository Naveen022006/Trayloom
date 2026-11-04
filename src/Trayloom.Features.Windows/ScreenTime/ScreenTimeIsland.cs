// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>The "Screen time" page of the expanded island (spec 06 section 1.5).</summary>
public sealed class ScreenTimeIslandSection : IIslandSection
{
    public string? FeatureId => ScreenTimeKeys.FeatureId;

    public int Order => 120;

    public string Id => ScreenTimeKeys.ModuleId;

    public string Title => ScreenTimeStrings.Title;

    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<ScreenTimeRuntime>() is not { } runtime)
        {
            return ScreenTimePageView.Message(ScreenTimeStrings.NotRunning);
        }

        return new ScreenTimePageView(runtime, context);
    }
}

/// <summary>Idle content of the closed island: the clock glyph and today's total (spec 06 section 1.6).</summary>
public sealed class ScreenTimeRestingStrip : IIslandStrip
{
    public string? FeatureId => ScreenTimeKeys.FeatureId;

    public int Order => 905;

    public string Id => "screenTime.resting";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<ScreenTimeRuntime>() is not { } runtime
            || !runtime.IsEnabled
            || context.App.Get(ScreenTimeKeys.IdleContent, "music") != ScreenTimeKeys.ModuleId)
        {
            return null;
        }

        return new ScreenTimeRestingView(runtime, context);
    }
}

/// <summary>The resting strip: updates itself every minute and asks the island to re-measure only when its width could have changed.</summary>
internal sealed class ScreenTimeRestingView : StackPanel
{
    private readonly ScreenTimeRuntime _runtime;
    private readonly IIslandContext _context;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };

    public ScreenTimeRestingView(ScreenTimeRuntime runtime, IIslandContext context)
    {
        _runtime = runtime;
        _context = context;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _text = ScreenTimeLook.Text(ScreenTimeFormat.Compact(runtime.Today.Total), 9, FontWeights.Medium, ScreenTimeLook.White(1), mono: true);
        _text.Margin = new Thickness(5, 0, 0, 0);
        Children.Add(ScreenTimeLook.Icon("", 11, ScreenTimeLook.Of(ScreenTimeLook.Tint)));
        Children.Add(_text);
        ScreenTimeLook.Tip(this, ScreenTimeStrings.ActionToday(ScreenTimeFormat.Long(runtime.Today.Total)));
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var text = ScreenTimeFormat.Compact(_runtime.Today.Total);
        if (text == _text.Text)
        {
            return;
        }

        var lengthChanged = text.Length != _text.Text.Length;
        _text.Text = text;
        if (lengthChanged)
        {
            _context.Invalidate();
        }
    }
}

/// <summary>The page: total, hour bars and the app list. Reads only in-memory data and redraws when the runtime says the numbers moved.</summary>
internal sealed class ScreenTimePageView : Border
{
    private const int ShownApps = 5;

    private readonly ScreenTimeRuntime _runtime;
    private readonly IIslandContext _context;
    private bool _expanded;

    public ScreenTimePageView(ScreenTimeRuntime runtime, IIslandContext context)
    {
        _runtime = runtime;
        _context = context;
        Child = Build();
        Loaded += (_, _) =>
        {
            // The page was just built from the same in-memory day, so there is nothing to redraw until the runtime reports a change.
            runtime.Changed += OnChanged;
        };
        Unloaded += (_, _) => runtime.Changed -= OnChanged;
    }

    /// <summary>A centred line for states without data.</summary>
    public static FrameworkElement Message(string text) => Centered("", text, null, null);

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(Rebuild));

    private void Rebuild()
    {
        Child = Build();
        _context.Invalidate();
    }

    private FrameworkElement Build()
    {
        if (!_runtime.IsEnabled)
        {
            var turnOn = ScreenTimeLook.FlatButton(ScreenTimeLook.Text(ScreenTimeStrings.TurnOn, 12, FontWeights.Medium), () => _context.App.Set(ScreenTimeKeys.Enabled, true), "ScreenTime.TurnOn", ScreenTimeStrings.TurnOn);
            return Centered("", ScreenTimeStrings.Off, null, turnOn);
        }

        var today = _runtime.Today;
        if (today.Total < 1)
        {
            return Centered("", ScreenTimeStrings.Empty, ScreenTimeStrings.EmptyCaption, null);
        }

        var root = new StackPanel();
        root.Children.Add(Header(today));
        root.Children.Add(Bars(today));
        root.Children.Add(Apps(today));
        return root;
    }

    private static FrameworkElement Centered(string glyph, string text, string? caption, UIElement? action)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        var icon = ScreenTimeLook.Icon(glyph, 22, ScreenTimeLook.White(0.5));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(icon);
        var line = ScreenTimeLook.Text(text, 13, null, ScreenTimeLook.White(0.9));
        line.Margin = new Thickness(0, 8, 0, 0);
        line.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(line);
        if (caption is not null)
        {
            var small = ScreenTimeLook.Text(caption, 11, null, ScreenTimeLook.White(0.6));
            small.Margin = new Thickness(0, 4, 0, 0);
            small.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(small);
        }

        if (action is FrameworkElement button)
        {
            button.Margin = new Thickness(0, 10, 0, 0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(button);
        }

        return panel;
    }

    private FrameworkElement Header(DayUsage today)
    {
        var grid = new Grid { Height = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var totalText = ScreenTimeFormat.Long(today.Total);
        var total = ScreenTimeLook.Text(totalText, 22, FontWeights.SemiBold, ScreenTimeLook.White(1), mono: true);
        ScreenTimeLook.Name(total, "ScreenTime.Total", totalText);
        left.Children.Add(total);
        var sub = ScreenTimeStrings.Today;
        if (_runtime.YesterdayTotal is { } yesterday)
        {
            var diff = today.Total - yesterday;
            sub += " · " + (Math.Abs(diff) < 60
                ? ScreenTimeStrings.SameAsYesterday
                : diff > 0 ? ScreenTimeStrings.MoreThanYesterday(ScreenTimeFormat.Long(diff)) : ScreenTimeStrings.LessThanYesterday(ScreenTimeFormat.Long(-diff)));
        }

        left.Children.Add(ScreenTimeLook.Text(sub, 11, null, ScreenTimeLook.White(0.6)));
        grid.Children.Add(left);
        if (today.First is not null)
        {
            var since = ScreenTimeLook.Text(ScreenTimeStrings.Since(ScreenTimeFormat.FirstUse(today.First)), 11, null, ScreenTimeLook.White(0.6));
            since.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(since, 1);
            grid.Children.Add(since);
        }

        return grid;
    }

    private static FrameworkElement Bars(DayUsage today)
    {
        const double barHeight = 44;
        var current = DateTime.Now.Hour;
        var isToday = today.Date == DateTime.Today;
        var bars = new Grid { Height = barHeight };
        var labels = new Grid { Height = 14, Margin = new Thickness(0, 2, 0, 0) };
        for (var h = 0; h < 24; h++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var seconds = today.Hours[h];
            var cell = new Grid { Background = Brushes.Transparent };
            var bar = new Border
            {
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(2),
                Height = seconds < 1 ? 2 : Math.Max(2, Math.Min(1, seconds / 3600) * barHeight),
                Background = seconds < 1 ? ScreenTimeLook.White(0.12) : ScreenTimeLook.Of(ScreenTimeLook.Tint, isToday && h == current ? 1 : 0.55),
            };
            cell.Children.Add(bar);
            ScreenTimeLook.Tip(cell, ScreenTimeStrings.HourTip(ScreenTimeFormat.HourRange(h), ScreenTimeFormat.Long(seconds)));
            Grid.SetColumn(cell, h);
            bars.Children.Add(cell);
            if (h % 6 == 0)
            {
                var label = ScreenTimeLook.Text(ScreenTimeFormat.HourLabel(h), 9.5, null, ScreenTimeLook.White(0.6));
                label.Margin = new Thickness(1, 0, 0, 0);
                Grid.SetColumn(label, h);
                Grid.SetColumnSpan(label, 4);
                labels.Children.Add(label);
            }
        }

        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(ScreenTimeLook.Expose(bars, "ScreenTime.Hours", ScreenTimeStrings.Today));
        stack.Children.Add(labels);
        return stack;
    }

    private FrameworkElement Apps(DayUsage today)
    {
        var ranked = today.Ranked();
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var top = ranked.Count == 0 ? 1 : ranked[0].Seconds;
        var shown = _expanded ? ranked.Count : Math.Min(ShownApps, ranked.Count);
        for (var i = 0; i < shown; i++)
        {
            list.Children.Add(AppRow(ranked[i], top));
        }

        if (ranked.Count > ShownApps)
        {
            var rest = ranked.Skip(ShownApps).Sum(a => a.Seconds);
            var text = _expanded ? ScreenTimeStrings.ShowFewer : ScreenTimeStrings.MoreApps(ranked.Count - ShownApps, ScreenTimeFormat.Long(rest));
            var more = ScreenTimeLook.FlatButton(ScreenTimeLook.Text(text, 11.5, null, ScreenTimeLook.White(0.6)), () =>
            {
                _expanded = !_expanded;
                Rebuild();
            }, "ScreenTime.More", text, radius: 6, padding: new Thickness(0, 4, 8, 4), fill: 0);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(more);
        }

        var host = ScreenTimeLook.Expose(list, "ScreenTime.AppList", ScreenTimeStrings.Title);
        host.Margin = new Thickness(0, 10, 0, 0);
        list.Margin = new Thickness(0);
        return host;
    }

    private static FrameworkElement AppRow(AppUsage app, double top)
    {
        var row = new Grid { Height = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (app.Id.StartsWith('#'))
        {
            icon = ScreenTimeLook.Icon(app.Id == ScreenTimeKeys.Shell ? "" : "", 14, ScreenTimeLook.White(0.8));
        }
        else if (AppIdentity.IconOf(app.Id) is { } source)
        {
            icon = new Image { Source = source, Width = 18, Height = 18, Stretch = Stretch.Uniform };
        }
        else
        {
            icon = ScreenTimeLook.Icon("", 14, ScreenTimeLook.White(0.8));
        }

        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(icon);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(ScreenTimeLook.Text(app.Name, 12.5, null, ScreenTimeLook.White(1), ellipsis: true));
        var fraction = Math.Clamp(app.Seconds / top, 0.02, 1);
        var track = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        track.Children.Add(new Border { Background = ScreenTimeLook.Of(ScreenTimeLook.Tint, 0.8), CornerRadius = new CornerRadius(2) });
        middle.Children.Add(track);
        Grid.SetColumn(middle, 2);
        row.Children.Add(middle);

        var duration = ScreenTimeLook.Text(ScreenTimeFormat.Compact(app.Seconds), 11.5, null, ScreenTimeLook.White(0.9), mono: true);
        duration.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(duration, 3);
        row.Children.Add(duration);
        row.Background = Brushes.Transparent;
        ScreenTimeLook.Tip(row, app.Name + "\n" + ScreenTimeFormat.Long(app.Seconds));
        return row;
    }
}
