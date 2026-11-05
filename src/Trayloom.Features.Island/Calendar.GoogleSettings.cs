// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>The "Google Calendar" part of Settings › Dynamic Island › Content › Calendar.</summary>
internal static class GoogleCalendarSettings
{
    /// <summary>Appends the source picker, one row per added calendar, the add and refresh buttons and the check interval.</summary>
    public static void Add(SettingsCard card, IAppSettings app, CalendarService? svc, ISettingsContext c)
    {
        card.Add(UiKit.Text(Loc.T("Google Calendar"), 12.5, FontWeights.Medium, ThemeKeys.TextPrimary));

        var modes = new List<(string Value, string Label)>
        {
            ("auto", Loc.T("Automatic")),
            ("google", Loc.T("Google Calendar")),
            ("windows", Loc.T("Windows Calendar")),
            ("both", Loc.T("Google and Windows")),
        };
        var current = (app.Get<string>(GoogleCalendarKeys.Source) ?? "auto").ToLowerInvariant();
        if (!modes.Any(m => m.Value == current))
        {
            current = "auto";
        }

        card.Add(new PickerRow<string>("", Loc.T("Show events from"), Loc.T("Automatic uses Google Calendar once you add one, and Windows Calendar until then."), modes, current, v =>
        {
            app.Set(GoogleCalendarKeys.Source, v);

            // Which Windows rows exist depends on the mode, so the page is built again.
            c.RequestRebuild();
        }, 170));

        if (svc is not null)
        {
            var source = svc.Google;
            foreach (var feed in source.Feeds)
            {
                card.Add(FeedRow(feed, source, c));
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var add = UiKit.Button(Loc.T("Add Google calendar…"), ButtonKind.Bordered, 11.5, null, () =>
            {
                var dialog = new GoogleCalendarWindow(source, Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive));
                dialog.ShowDialog();
                if (dialog.Added)
                {
                    c.RequestRebuild();
                }
            });
            buttons.Children.Add(add);
            if (source.Feeds.Count > 0)
            {
                var refresh = UiKit.Button(Loc.T("Refresh now"), ButtonKind.Bordered, 11.5, null, source.RefreshNow);
                refresh.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(refresh);
            }

            card.Add(buttons);

            // Statuses change in the background (a check finishing, an address being refused), so the page follows while it is open.
            void OnChanged() => card.Dispatcher.BeginInvoke(c.RequestRebuild);
            card.Loaded += (_, _) => source.Changed += OnChanged;
            card.Unloaded += (_, _) => source.Changed -= OnChanged;
        }

        var intervals = new List<(int Value, string Label)>
        {
            (5, Loc.F("{0} minutes", 5)),
            (15, Loc.F("{0} minutes", 15)),
            (30, Loc.F("{0} minutes", 30)),
            (60, Loc.T("1 hour")),
        };
        var minutes = app.Get<int>(GoogleCalendarKeys.Interval);
        card.Add(new PickerRow<int>("", Loc.T("Check for changes every"), Loc.T("Google can take a few minutes to update this address after you change an event."), intervals, intervals.Any(i => i.Value == minutes) ? minutes : 15, v => app.Set(GoogleCalendarKeys.Interval, v), 140));
        card.AddCaption(Loc.T("Events come from Google over HTTPS and are kept in memory and in an encrypted cache on this PC. Nothing is sent anywhere else."));
    }

    private static FrameworkElement FeedRow(GoogleFeed feed, GoogleCalendarSource source, ISettingsContext c)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(GoogleRules.ParseColor(feed.Color)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Calendar colour"),
        };
        dot.Clickable(() => PaletteMenu(dot, feed, source).IsOpen = true, Loc.T("Calendar colour"));
        grid.Children.Add(dot);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = UiKit.Text(feed.Name, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        text.Children.Add(name);
        var status = source.StatusOf(feed.Id);
        var rejected = status.State == FeedState.Rejected;
        text.Children.Add(UiKit.Text(StatusText(status), 11, FontWeights.Normal, rejected ? ThemeKeys.Orange : ThemeKeys.TextSecondary, wrap: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(UiKit.Button(Loc.T("Rename…"), ButtonKind.Bordered, 11.5, null, () => BeginRename(text, name, feed, source, c)));
        var remove = UiKit.Button(Loc.T("Remove"), ButtonKind.Bordered, 11.5, null, () =>
        {
            source.Remove(feed.Id);
            c.RequestRebuild();
        });
        remove.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(remove);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    /// <summary>Swaps the name for a text box; Enter or leaving the box saves, Escape keeps the old name.</summary>
    private static void BeginRename(StackPanel host, TextBlock name, GoogleFeed feed, GoogleCalendarSource source, ISettingsContext c)
    {
        var box = new TextBox { Text = feed.Name, MaxLength = 80, MinWidth = 160, FontSize = 12.5 };
        var done = false;
        void Finish(bool save)
        {
            if (done)
            {
                return;
            }

            done = true;
            if (save)
            {
                source.Rename(feed.Id, box.Text);
            }

            c.RequestRebuild();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        var index = host.Children.IndexOf(name);
        if (index < 0)
        {
            return;
        }

        host.Children.RemoveAt(index);
        host.Children.Insert(index, box);
        box.Focus();
        box.SelectAll();
    }

    private static ContextMenu PaletteMenu(FrameworkElement target, GoogleFeed feed, GoogleCalendarSource source)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (name, hex) in GoogleRules.Palette)
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(GoogleRules.ParseColor(hex)), Margin = new Thickness(0, 0, 8, 0) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock { Text = ColorName(name), VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, IsChecked = string.Equals(hex, feed.Color, StringComparison.OrdinalIgnoreCase) };
            var chosen = hex;
            item.Click += (_, _) => source.SetColor(feed.Id, chosen);
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>The localized name of a palette colour (literal strings so the translation scan finds them).</summary>
    private static string ColorName(string name) => name switch
    {
        "Peacock" => Loc.T("Peacock"),
        "Tomato" => Loc.T("Tomato"),
        "Sage" => Loc.T("Sage"),
        "Banana" => Loc.T("Banana"),
        "Grape" => Loc.T("Grape"),
        "Tangerine" => Loc.T("Tangerine"),
        "Lavender" => Loc.T("Lavender"),
        "Basil" => Loc.T("Basil"),
        "Flamingo" => Loc.T("Flamingo"),
        "Blueberry" => Loc.T("Blueberry"),
        "Graphite" => Loc.T("Graphite"),
        "Cocoa" => Loc.T("Cocoa"),
        _ => name,
    };

    private static string StatusText(FeedStatus status)
    {
        switch (status.State)
        {
            case FeedState.Rejected:
                return Loc.T("Google no longer accepts this address. Copy it again from Google Calendar.");
            case FeedState.Ok:
                return status.LastOkUtc is { } ok ? Loc.F("Updated {0}", Stamp(ok)) : Loc.T("Waiting for the first update…");
            default:
                return status.LastOkUtc is { } last ? Loc.F("Offline. Showing what was loaded at {0}.", Stamp(last)) : Loc.T("Could not reach Google. Check your connection and try again.");
        }
    }

    /// <summary>Today's time of day, or the date and time for an older stamp.</summary>
    private static string Stamp(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("t") : local.ToString("g");
    }
}
