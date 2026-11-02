// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Windows.ApplicationModel.Appointments;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>A month calendar inside the island (local only, no account sync).</summary>
public sealed class IslandCalendarFeature : IFeature
{
    private IFeatureContext? _context;
    private CalendarView? _view;

    public string Id => "island-calendar";

    public string Name => "Island calendar";

    public string Description => "See the month at a glance in the island, with today highlighted and upcoming Windows calendar events when available.";

    public FeatureGroup Group => FeatureGroup.Island;

    public IReadOnlyList<FeatureAction> Actions =>
        IslandHub.IsRunning
            ? [new FeatureAction("open", "Open the island calendar", () => IslandHub.Controller?.Expand("calendar"), "")]
            : [];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _view = new CalendarView(context);
        var view = _view;
        IslandHub.RegisterSection(new IslandSection
        {
            Id = "calendar",
            Title = "Calendar",
            Glyph = "",
            Order = 20,
            Create = view.Build,
            VisibilityChanged = view.SetVisible,
        });
    }

    public void Stop()
    {
        IslandHub.UnregisterSection("calendar");
        _view = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddChoice(
                "weekStart",
                "Week starts on",
                new Choice<string>[] { new("auto", "System default"), new("monday", "Monday"), new("sunday", "Sunday") },
                "auto")
            .AddToggle("showEvents", "Show upcoming Windows calendar events", true, "Only works when Windows allows this app to read your calendar; nothing is synced or stored.")
            .Build();
    }
}

internal sealed class CalendarView
{
    private readonly IFeatureContext _context;
    private readonly Grid _grid = new();
    private readonly TextBlock _monthTitle = IslandTheme.Label(string.Empty, 15, IslandTheme.Text, FontWeights.SemiBold);
    private readonly TextBlock _bigDay = IslandTheme.Digits(54, IslandTheme.Text, FontWeights.Light);
    private readonly TextBlock _weekday = IslandTheme.Label(string.Empty, 15, IslandTheme.Text, FontWeights.SemiBold);
    private readonly TextBlock _monthYear = IslandTheme.Label(string.Empty, 13, IslandTheme.Muted);
    private readonly TextBlock _relative = IslandTheme.Label(string.Empty, 12, IslandTheme.Muted);
    private readonly StackPanel _events = new() { Margin = new Thickness(0, 10, 0, 0) };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selected = DateTime.Today;
    private IReadOnlyList<(DateTime Start, string Subject)> _appointments = [];
    private DateTime _appointmentsLoaded = DateTime.MinValue;
    private bool _eventsUnavailable;
    private bool _visible;

    public CalendarView(IFeatureContext context)
    {
        _context = context;
    }

    private DayOfWeek FirstDay => _context.Settings.Get("weekStart", "auto") switch
    {
        "monday" => DayOfWeek.Monday,
        "sunday" => DayOfWeek.Sunday,
        _ => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
    };

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible)
        {
            _selected = DateTime.Today;
            _month = new DateTime(_selected.Year, _selected.Month, 1);
            Render();
            _ = LoadAppointmentsAsync();
        }
    }

    public FrameworkElement Build()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        // Left: month grid
        var left = new Grid { Margin = new Thickness(0, 0, 14, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var nav = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previous = new IslandButton("", size: 28, foreground: IslandTheme.Muted);
        var today = new IslandButton(null, "Today", 28, foreground: IslandTheme.Muted);
        var next = new IslandButton("", size: 28, foreground: IslandTheme.Muted);
        previous.Clicked += () => MoveMonth(-1);
        next.Clicked += () => MoveMonth(1);
        today.Clicked += () =>
        {
            _selected = DateTime.Today;
            _month = new DateTime(_selected.Year, _selected.Month, 1);
            Render();
        };
        Grid.SetColumn(previous, 1);
        Grid.SetColumn(today, 2);
        Grid.SetColumn(next, 3);
        nav.Children.Add(_monthTitle);
        nav.Children.Add(previous);
        nav.Children.Add(today);
        nav.Children.Add(next);

        for (var i = 0; i < 7; i++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 6; i++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        Grid.SetRow(_grid, 1);
        left.Children.Add(nav);
        left.Children.Add(_grid);

        // Right: selected day
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(_bigDay);
        right.Children.Add(_weekday);
        right.Children.Add(_monthYear);
        right.Children.Add(_relative);
        right.Children.Add(_events);
        Grid.SetColumn(right, 1);

        var divider = new Border { Width = 1, Background = IslandTheme.SurfaceHover, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };

        root.Children.Add(left);
        root.Children.Add(divider);
        Grid.SetColumn(divider, 0);
        root.Children.Add(right);
        Render();
        return root;
    }

    private void MoveMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        Render();
    }

    private void Render()
    {
        if (_grid.ColumnDefinitions.Count == 0)
        {
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        _monthTitle.Text = _month.ToString(_month.Year == DateTime.Today.Year ? "MMMM" : "MMMM yyyy", culture);

        _grid.Children.Clear();
        var first = FirstDay;
        for (var c = 0; c < 7; c++)
        {
            var day = (DayOfWeek)(((int)first + c) % 7);
            var name = culture.DateTimeFormat.GetShortestDayName(day);
            var header = IslandTheme.Label(name, 11, IslandTheme.Faint, FontWeights.SemiBold);
            header.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(header, c);
            _grid.Children.Add(header);
        }

        var offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        var start = _month.AddDays(-offset);
        var todayDate = DateTime.Today;
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Month == _month.Month;
            var isToday = date == todayDate;
            var isSelected = date == _selected;

            var text = IslandTheme.Digits(12.5, isToday ? IslandTheme.Text : inMonth ? IslandTheme.Text : IslandTheme.Faint, isToday ? FontWeights.Bold : FontWeights.Normal);
            text.Text = date.Day.ToString(culture);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border
            {
                Child = text,
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = isToday ? IslandTheme.Red : isSelected ? IslandTheme.SurfacePressed : Brushes.Transparent,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (!isToday)
            {
                cell.MouseEnter += (_, _) => cell.Background = IslandTheme.SurfaceHover;
                cell.MouseLeave += (_, _) => cell.Background = isSelected ? IslandTheme.SurfacePressed : Brushes.Transparent;
            }

            var captured = date;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                _selected = captured;
                if (captured.Month != _month.Month)
                {
                    _month = new DateTime(captured.Year, captured.Month, 1);
                }

                Render();
                e.Handled = true;
            };
            Grid.SetRow(cell, 1 + (i / 7));
            Grid.SetColumn(cell, i % 7);
            _grid.Children.Add(cell);
        }

        _bigDay.Text = _selected.Day.ToString(culture);
        _weekday.Text = _selected.ToString("dddd", culture);
        _monthYear.Text = _selected.ToString("MMMM yyyy", culture);
        var days = (_selected.Date - todayDate).Days;
        var week = culture.Calendar.GetWeekOfYear(_selected, culture.DateTimeFormat.CalendarWeekRule, first);
        _relative.Text = (days == 0 ? "Today" : days == 1 ? "Tomorrow" : days == -1 ? "Yesterday" : days > 0 ? $"In {days} days" : $"{-days} days ago") + $" · week {week}";
        RenderEvents();
    }

    private void RenderEvents()
    {
        _events.Children.Clear();
        if (!_context.Settings.Get("showEvents", true) || _eventsUnavailable)
        {
            return;
        }

        var day = _appointments.Where(a => a.Start.Date == _selected.Date).Take(3).ToList();
        foreach (var (time, subject) in day)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(IslandTheme.Label(subject, 12.5, IslandTheme.Text, FontWeights.SemiBold));
            row.Children.Add(IslandTheme.Label(time.ToString("t", CultureInfo.CurrentCulture), 11.5, IslandTheme.Muted));
            _events.Children.Add(row);
        }

        if (day.Count == 0 && _appointmentsLoaded != DateTime.MinValue)
        {
            _events.Children.Add(IslandTheme.Label("No events", 12, IslandTheme.Faint));
        }
    }

    /// <summary>Reads the next two weeks of events from the Windows calendar store when the OS allows it (unpackaged apps usually are refused).</summary>
    private async Task LoadAppointmentsAsync()
    {
        if (_eventsUnavailable || !_context.Settings.Get("showEvents", true) || DateTime.Now - _appointmentsLoaded < TimeSpan.FromMinutes(5))
        {
            return;
        }

        try
        {
            var work = ReadAppointmentsAsync();
            var finished = await Task.WhenAny(work, Task.Delay(4000));
            if (finished != work)
            {
                _eventsUnavailable = true;
                return;
            }

            _appointments = await work;
            _appointmentsLoaded = DateTime.Now;
        }
        catch (Exception ex)
        {
            _eventsUnavailable = true;
            _context.Log.Info($"Windows calendar events are not available: {ex.Message}");
        }

        if (_visible)
        {
            RenderEvents();
        }
    }

    private static async Task<IReadOnlyList<(DateTime Start, string Subject)>> ReadAppointmentsAsync()
    {
        var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly)
            ?? throw new InvalidOperationException("Calendar store refused access.");
        var from = new DateTimeOffset(DateTime.Today.AddDays(-7));
        var found = await store.FindAppointmentsAsync(from, TimeSpan.FromDays(60));
        return found.OrderBy(a => a.StartTime).Select(a => (a.StartTime.LocalDateTime, a.Subject ?? string.Empty)).ToList();
    }
}
