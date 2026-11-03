// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

internal sealed class CalendarPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private DateTime _focus = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _selected;
    private bool _monthShort;
    private double _w = 400;
    private double _h = 264;
    private bool _requesting;
    private static readonly Brush CalAccent = D.CalendarAccent;

    public CalendarPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "calendar";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override bool ScrollsContent => true;

    private bool Tall => _h >= 300 && _w >= 420;

    private DayOfWeek First => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    public override double Height(double width, double budget)
    {
        _w = width;
        _h = budget;
        Build();
        return budget;
    }

    public override void Shown()
    {
        Core.Calendar.Changed += OnData;
        if (Core.Calendar.Access == CalendarAccess.Unknown)
        {
            _ = Core.Calendar.RequestAccessAsync();
        }

        RequestRange();
    }

    public override void Hidden() => Core.Calendar.Changed -= OnData;

    public override bool HasLayers => true;

    public override bool CanStepBack => _monthShort;

    public override bool StepBack()
    {
        if (_monthShort)
        {
            _monthShort = false;
            Build();
            return true;
        }

        return false;
    }

    private void OnData() => _view.Dispatcher.BeginInvoke(DispatcherPriority.Background, Build);

    private DateTime GridStart()
    {
        var first = _focus;
        var back = ((int)first.DayOfWeek - (int)First + 7) % 7;
        return first.AddDays(-back);
    }

    private void RequestRange()
    {
        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(8);
        if (Tall || _monthShort)
        {
            var gs = GridStart();
            from = gs < from ? gs : from;
            var ge = gs.AddDays(42);
            to = ge > to ? ge : to;
        }

        Core.Calendar.WantRange(from, to);
    }

    private void Build()
    {
        _view.Children.Clear();
        BuildBand();
        RequestRange();
        if (Core.Calendar.Access != CalendarAccess.Allowed && Core.Calendar.Access != CalendarAccess.Unknown)
        {
            _view.Children.Add(PermissionCard());
            return;
        }

        if (Tall)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var month = PageUi.VScroll(MonthGrid(tall: true, 196, 0));
            grid.Children.Add(month);
            var div = new Border { Background = D.White(0.12), Margin = new Thickness(8, 0, 8, 0), Width = 1 };
            Grid.SetColumn(div, 1);
            div.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(div);
            var agenda = new Grid { Margin = new Thickness(16, 0, 0, 0) };
            agenda.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            agenda.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = AgendaHeader();
            agenda.Children.Add(header);
            var list = Agenda();
            Grid.SetRow(list, 1);
            agenda.Children.Add(list);
            Grid.SetColumn(agenda, 2);
            grid.Children.Add(agenda);
            _view.Children.Add(grid);
        }
        else if (_monthShort)
        {
            _view.Children.Add(MonthGrid(tall: false, _w, _h));
        }
        else
        {
            var stack = new Grid();
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            stack.Children.Add(WeekStrip());
            var list = Agenda();
            list.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(list, 1);
            stack.Children.Add(list);
            _view.Children.Add(stack);
        }
    }

    private void BuildBand()
    {
        _leading.Children.Clear();
        _trailing.Children.Clear();
        var title = _focus.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        if (Tall)
        {
            _leading.Children.Add(D.T(title, 13, FontWeights.SemiBold));
        }
        else
        {
            var b = new HoverButton(() => { _monthShort = !_monthShort; Build(); }) { Height = 28, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 0), Selected = _monthShort, Lifts = false };
            b.Child = D.T(title, 13, FontWeights.SemiBold);
            D.Tip(b, Loc.T("Month"));
            _leading.Children.Add(b);
        }

        var prevTitle = Tall || _monthShort ? Loc.T("Previous month") : Loc.T("Previous week");
        var nextTitle = Tall || _monthShort ? Loc.T("Next month") : Loc.T("Next week");
        _trailing.Children.Add(D.IconButton(G.ChevronLeft, prevTitle, () => Step(-1)));
        var todayBtn = D.IconButton("x", Loc.T("Today"), GoToday);
        var ringGrid = new Grid { Width = 14, Height = 14 };
        ringGrid.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = D.White(0.75), StrokeThickness = 1.3 });
        ringGrid.Children.Add(new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Fill = D.White(0.75) });
        todayBtn.Child = ringGrid;
        _trailing.Children.Add(todayBtn);
        _trailing.Children.Add(D.IconButton(G.ChevronRight, nextTitle, () => Step(1)));
        if (!Tall || true)
        {
            var open = D.IconButton(G.OpenApp, Loc.T("Open Calendar"), () => OpenCalendar());
            open.Margin = new Thickness(6, 0, 0, 0);
            _trailing.Children.Add(open);
        }
    }

    private void OpenCalendar()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("outlookcal:") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void GoToday()
    {
        _focus = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selected = null;
        _weekOffset = 0;
        Build();
    }

    private int _weekOffset;

    private void Step(int dir)
    {
        if (Tall || _monthShort)
        {
            _focus = _focus.AddMonths(dir);
        }
        else
        {
            _weekOffset += dir;
        }

        Build();
    }

    // ---------------------------------------------------------------- month grid

    private FrameworkElement MonthGrid(bool tall, double width, double height)
    {
        var weekNumbers = Core.App.Get<bool>(K.CalendarWeekNumbers);
        var cols = weekNumbers ? 8 : 7;
        var grid = new Grid { Width = tall ? 196 : width };
        if (weekNumbers)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        }

        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rowH = tall ? 30.0 : Math.Min(30, Math.Max(16, Math.Floor((height - 14) / 6)));
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tall ? 18 : 14) });
        for (var r = 0; r < 6; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowH + (tall ? 4 : 0)) });
        }

        var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
        for (var i = 0; i < 7; i++)
        {
            var name = names[((int)First + i) % 7];
            var t = D.T(name, 9, FontWeights.Medium, D.White(0.45));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, (weekNumbers ? 1 : 0) + i);
            grid.Children.Add(t);
        }

        var start = GridStart();
        for (var r = 0; r < 6; r++)
        {
            if (weekNumbers)
            {
                var wk = ISOWeek.GetWeekOfYear(start.AddDays((r * 7) + 3));
                var wt = D.Digits(wk.ToString(), 9, FontWeights.Medium, D.White(0.45));
                wt.HorizontalAlignment = HorizontalAlignment.Center;
                wt.VerticalAlignment = VerticalAlignment.Top;
                wt.Margin = new Thickness(0, 4, 0, 0);
                Grid.SetRow(wt, r + 1);
                grid.Children.Add(wt);
            }

            for (var c = 0; c < 7; c++)
            {
                var day = start.AddDays((r * 7) + c);
                var cell = DayCell(day, rowH, tall ? 24 : Math.Min(24, rowH - 3), tall ? 11 : Math.Min(11, (rowH - 3) * 0.62), tall);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, (weekNumbers ? 1 : 0) + c);
                grid.Children.Add(cell);
            }
        }

        _ = cols;
        return grid;
    }

    private FrameworkElement DayCell(DateTime day, double rowH, double circle, double fontSize, bool dots)
    {
        var today = day.Date == DateTime.Today;
        var selected = _selected?.Date == day.Date;
        var inMonth = day.Month == _focus.Month;
        var b = new HoverButton(() => { _selected = day; if (_monthShort) { _monthShort = false; } Build(); }) { CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 0, 1, 0), Lifts = false };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Brush fill = Brushes.Transparent;
        Brush fg = D.White(inMonth ? 1 : 0.4);
        if (today)
        {
            fill = CalAccent;
            fg = Brushes.White;
        }
        else if (selected)
        {
            fill = Brushes.White;
            fg = Brushes.Black;
        }

        var circ = new Border { Width = circle, Height = circle, CornerRadius = new CornerRadius(circle / 2), Background = fill };
        if (today && selected)
        {
            circ.BorderBrush = D.White(0.8);
            circ.BorderThickness = new Thickness(1.5);
        }

        var num = D.T(day.Day.ToString(), fontSize, today || selected ? FontWeights.Bold : FontWeights.Normal, fg);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        circ.Child = num;
        stack.Children.Add(circ);
        if (dots)
        {
            var ev = Core.Calendar.On(day).Where(e => !e.AllDay || true).Select(e => e.Color).Distinct().Take(3).ToList();
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 3, Margin = new Thickness(0, 2, 0, 0) };
            foreach (var c in ev)
            {
                row.Children.Add(new Ellipse_ { Width = 3, Height = 3, Margin = new Thickness(1, 0, 1, 0), Fill = new SolidColorBrush(c) });
            }

            stack.Children.Add(row);
        }

        b.Child = stack;
        D.Tip(b, day.ToString("D", CultureInfo.CurrentCulture));
        return b;
    }

    private FrameworkElement WeekStrip()
    {
        var grid = new Grid { Height = 52 };
        var weekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek - (int)First + 7) % 7) + (_weekOffset * 7));
        var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = weekStart.AddDays(i);
            var today = day.Date == DateTime.Today;
            var selected = _selected?.Date == day.Date;
            var b = new HoverButton(() => { _selected = selected ? null : day; Build(); }) { CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, 2, 0), Lifts = false };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var nm = D.T(names[(int)day.DayOfWeek], 9, FontWeights.Medium, D.White(0.45));
            nm.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(nm);
            Brush fill = today ? CalAccent : (selected ? Brushes.White : Brushes.Transparent);
            Brush fg = today ? Brushes.White : (selected ? Brushes.Black : D.Text);
            var circ = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = fill, Margin = new Thickness(0, 2, 0, 0), BorderThickness = new Thickness(today && selected ? 1.5 : 0), BorderBrush = D.White(0.8) };
            var num = D.T(day.Day.ToString(), 12, today || selected ? FontWeights.Bold : FontWeights.Normal, fg);
            num.HorizontalAlignment = num.VerticalAlignment == VerticalAlignment.Center ? HorizontalAlignment.Center : HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            circ.Child = num;
            stack.Children.Add(circ);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 3, Margin = new Thickness(0, 3, 0, 0) };
            foreach (var c in Core.Calendar.On(day).Select(e => e.Color).Distinct().Take(3))
            {
                dots.Children.Add(new Ellipse_ { Width = 3, Height = 3, Margin = new Thickness(1, 0, 1, 0), Fill = new SolidColorBrush(c) });
            }

            stack.Children.Add(dots);
            b.Child = stack;
            D.Tip(b, day.ToString("D", CultureInfo.CurrentCulture));
            Grid.SetColumn(b, i);
            grid.Children.Add(b);
        }

        return grid;
    }

    // ---------------------------------------------------------------- agenda

    private FrameworkElement AgendaHeader()
    {
        var g = new Grid { MinHeight = 32 };
        var text = _selected is { } d ? d.ToString("ddd d MMM", CultureInfo.CurrentCulture) : Loc.T("Next 7 days");
        var t = D.T(text, 13, FontWeights.SemiBold, D.Text, true);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.MaxHeight = 36;
        g.Children.Add(t);
        if (_selected is not null)
        {
            var back = D.IconButton("", Loc.T("Next 7 days"), () => { _selected = null; Build(); });
            back.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(back);
        }

        return g;
    }

    private FrameworkElement Agenda()
    {
        var cal = Core.Calendar;
        var stack = new StackPanel();
        var now = DateTime.Now;
        var days = new List<(DateTime Day, List<CalEvent> Events)>();
        if (_selected is { } sel)
        {
            days.Add((sel.Date, [.. cal.On(sel)]));
        }
        else
        {
            for (var i = 0; i < 7; i++)
            {
                var day = DateTime.Today.AddDays(i);
                var list = cal.On(day).Where(e => e.End > now).ToList();
                if (list.Count > 0)
                {
                    days.Add((day, list));
                }
            }
        }

        if (cal.Loading && cal.Events.Count == 0)
        {
            var sp = PageUi.Spinner(16);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Margin = new Thickness(0, 24, 0, 0);
            return sp;
        }

        if (days.Sum(d => d.Events.Count) == 0)
        {
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
            var ic = D.I(G.Calendar, 26, D.White(0.45));
            ic.FontWeight = FontWeights.Light;
            box.Children.Add(ic);
            var tx = D.T(_selected is null ? Loc.T("No upcoming appointments") : Loc.T("No appointments on this day"), 12, null, D.White(0.65));
            tx.Margin = new Thickness(0, 8, 0, 0);
            tx.HorizontalAlignment = HorizontalAlignment.Center;
            box.Children.Add(tx);
            if (!Tall)
            {
                var open = D.PillButton(Loc.T("Open Calendar"), OpenCalendar, true);
                open.Margin = new Thickness(0, 8, 0, 0);
                open.HorizontalAlignment = HorizontalAlignment.Center;
                box.Children.Add(open);
            }

            return box;
        }

        var nextTimed = cal.Next(now);
        foreach (var (day, events) in days)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, stack.Children.Count > 0 ? 14 : 0, 0, 6) };
            var isToday = day == DateTime.Today;
            label.Children.Add(D.T(isToday ? Loc.T("Today") : day.ToString("ddd", CultureInfo.CurrentCulture), 11, FontWeights.Medium, isToday ? D.Text : D.White(0.5)));
            var dm = D.T("  " + day.ToString("d MMM", CultureInfo.CurrentCulture), 11, FontWeights.Medium, D.White(0.5));
            label.Children.Add(dm);
            stack.Children.Add(label);
            foreach (var e in events)
            {
                stack.Children.Add(EventCard(e, now, nextTimed));
            }
        }

        return PageUi.VScroll(stack);
    }

    private FrameworkElement EventCard(CalEvent e, DateTime now, CalEvent? next)
    {
        var ongoing = e.Ongoing(now);
        var b = new HoverButton(() => Core.Calendar.OpenEvent(e)) { CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 0, 7), Lifts = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var card = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = D.Tint(e.Color, ongoing ? 0.20 : 0.10),
            BorderBrush = D.White(ongoing ? 0.16 : 0.05),
            BorderThickness = new Thickness(0.75),
            Padding = new Thickness(12, 10, 10, 10),
        };
        var grid = new Grid();
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(e.Color), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 0, 0, 0) };
        grid.Children.Add(bar);
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        if (ongoing || (next is not null && ReferenceEquals(next, e) && !e.AllDay))
        {
            col.Children.Add(D.T(ongoing ? Loc.T("Happening now") : Loc.T("Up next"), 9, FontWeights.SemiBold, ongoing ? D.Mint : D.White(0.7)));
        }

        var ended = !e.AllDay && e.End <= now;
        col.Children.Add(D.T(e.Title.Length == 0 ? Loc.T("Untitled event") : e.Title, 13, FontWeights.SemiBold, D.White(ended ? 0.65 : 1), true));
        string time;
        if (e.AllDay)
        {
            time = Loc.T("All day");
        }
        else if (e.Start.Date == e.End.AddSeconds(-1).Date)
        {
            time = $"{e.Start:t} · {e.End:t}";
        }
        else
        {
            time = $"{e.Start:d MMM} {e.Start:t} → {e.End:d MMM} {e.End:t}";
        }

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var tt = D.Digits(time, 11, null, D.White(0.75));
        timeRow.Children.Add(tt);
        if (Core.Calendar.IsChosen(e))
        {
            var ic = D.I(G.Timer, 10, D.White(0.75));
            ic.Margin = new Thickness(6, 0, 0, 0);
            timeRow.Children.Add(ic);
        }

        col.Children.Add(timeRow);
        if (e.CalendarName.Length > 0)
        {
            col.Children.Add(D.T(e.CalendarName, 10, null, D.White(0.5), false, TextTrimming.CharacterEllipsis));
        }

        if (e.Location.Length > 0)
        {
            var loc = new StackPanel { Orientation = Orientation.Horizontal };
            loc.Children.Add(D.I(G.Mappin, 10, D.White(0.6)));
            var lt = D.T(e.Location, 10, null, D.White(0.6), false, TextTrimming.CharacterEllipsis);
            lt.Margin = new Thickness(4, 0, 0, 0);
            loc.Children.Add(lt);
            col.Children.Add(loc);
        }

        grid.Children.Add(col);
        card.Child = grid;
        b.Child = card;
        D.Tip(b, Loc.T("Open Calendar"));
        if (!e.AllDay && e.Start > now && !Core.App.Get<bool>(K.CalendarCountdown))
        {
            b.MouseRightButtonUp += (_, ev) =>
            {
                var chosen = Core.Calendar.IsChosen(e);
                IslandMenu.Show(b, [new IslandMenu.Item(chosen ? Loc.T("Remove Countdown") : Loc.T("Add Countdown"), () => { Core.Calendar.ToggleChosen(e); Build(); })], () => HasWindow ? Window.HoldOpen() : null);
                ev.Handled = true;
            };
        }

        return b;
    }

    private FrameworkElement PermissionCard()
    {
        var denied = Core.Calendar.Access == CalendarAccess.Denied;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ic = D.I("", 28, D.White(0.7));
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(ic);
        var msg = D.T(denied ? Loc.T("Allow calendar access in Windows Settings to see your appointments.") : Loc.T("Read your calendars to show upcoming appointments. Events stay on this PC."), 12, null, D.Secondary, true);
        msg.TextAlignment = TextAlignment.Center;
        msg.MaxWidth = 300;
        msg.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(msg);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (denied)
        {
            row.Children.Add(D.PillButton(Loc.T("Open Windows Settings"), () => OpenSettingsUri(), true));
        }

        var allow = D.PillButton(_requesting ? "…" : Loc.T("Allow Calendar Access"), async () =>
        {
            _requesting = true;
            Build();
            await Core.Calendar.RequestAccessAsync();
            _requesting = false;
            Build();
        }, true);
        allow.Margin = new Thickness(denied ? 6 : 0, 0, 0, 0);
        row.Children.Add(allow);
        stack.Children.Add(row);
        if (Core.Calendar.RequestFailed)
        {
            var f = D.T(Loc.T("Could not request calendar access. Please try again."), 11, null, D.Orange);
            f.Margin = new Thickness(0, 8, 0, 0);
            f.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(f);
        }

        return stack;
    }

    internal static void OpenSettingsUri()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-calendar") { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
