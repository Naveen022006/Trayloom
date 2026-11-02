// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>The Cleaner state machine view: idle / scanning / results / cleaning / done (spec 7.1.2).</summary>
internal sealed class CleanerView : ContentControl
{
    private static readonly HashSet<CleanerGroup> Expanded = [];
    private static bool _scheduleOpen;
    private readonly CleanerModel _m = CleanerModel.Shared;
    private readonly bool _compact;
    private readonly Action? _relayout;
    private readonly Action<bool>? _keepOpen;

    public CleanerView(bool compact, Action? relayout = null, Action<bool>? keepOpen = null)
    {
        _compact = compact;
        _relayout = relayout;
        _keepOpen = keepOpen;
        Loaded += (_, _) =>
        {
            _m.Changed += OnChanged;
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _m.Changed -= OnChanged;
            _keepOpen?.Invoke(false);
        };
        Rebuild();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);

    private static (string Glyph, string Title, string Caption) Meta(CleanerGroup group, int screenshotDays) => group switch
    {
        CleanerGroup.LoginItems => (G.Power, Loc.T("Orphaned startup items"), Loc.T("Startup entries left by apps that no longer exist.")),
        CleanerGroup.SafeCaches => (G.Package, Loc.T("Caches"), Loc.T("Temporary files apps rebuild on their own.")),
        CleanerGroup.Logs => (G.Document, Loc.T("Logs"), Loc.T("Old diagnostic logs.")),
        CleanerGroup.Developer => (G.Hammer, Loc.T("Developer junk"), Loc.T("Build and tooling leftovers that are rebuilt on demand.")),
        CleanerGroup.Leftovers => (G.Puzzle, Loc.T("Leftovers from uninstalled apps"), Loc.T("Files left behind by apps you uninstalled.")),
        CleanerGroup.OtherCaches => (G.Drive, Loc.T("Other caches"), Loc.T("Safe to remove, nothing breaks. Apps may open slower once and downloaded content, like offline music, downloads again.")),
        CleanerGroup.DeviceBackups => (G.Phone, Loc.T("iPhone backups"), Loc.T("Old iPhone and iPad backups take a big slice of the storage Windows counts as Other. Remove only the ones you no longer need; a new backup is made when you plug the device in again.")),
        CleanerGroup.Screenshots => (G.Camera, Loc.T("Forgotten screenshots"), Loc.F("Screenshots you have not opened in {0} days that still have the name and folder Windows gave them. Ones you renamed or moved never show up here.", screenshotDays)),
        _ => (G.Trash, Loc.T("Recycle Bin"), Loc.T("Emptying the Recycle Bin is permanent.")),
    };

    private void Rebuild()
    {
        switch (_m.Phase)
        {
            case CleanerPhase.Idle:
                Content = Idle();
                break;
            case CleanerPhase.Scanning:
                Content = Busy(Loc.T("Scanning…"), _m.ScanningCategory, true);
                break;
            case CleanerPhase.Cleaning:
                Content = Busy(Loc.T("Cleaning…"), null, false);
                break;
            case CleanerPhase.Results:
                Content = Results();
                break;
            default:
                Content = Done();
                break;
        }

        _keepOpen?.Invoke(_m.Phase is CleanerPhase.Scanning or CleanerPhase.Cleaning);
        _relayout?.Invoke();
    }

    private static TextBlock Sparkle(double size, bool animate)
    {
        var glyph = UiKit.Icon(G.Sparkle, size, ThemeKeys.TextSecondary);
        glyph.FontWeight = FontWeights.Light;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        if (animate)
        {
            glyph.Loaded += (_, _) => glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        return glyph;
    }

    // ------------------------------------------------------------------ idle

    private FrameworkElement Idle()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(_compact ? 14 : 28) };
        column.Children.Add(Sparkle(_compact ? 34 : 46, false));
        var title = UiKit.Text(Loc.T("Clean up your PC"), _compact ? 15 : 17, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(title, _compact ? 12 : 18);
        var caption = UiKit.Text(Loc.T("Scans for leftovers from uninstalled apps, caches, logs and the Recycle Bin. You review everything first and removed items go to the Recycle Bin or are deleted as described in the list."), 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.TextAlignment = TextAlignment.Center;
        caption.MaxWidth = 380;
        column.AddSpaced(caption, _compact ? 12 : 18);
        var scan = UiKit.Button(Loc.T("Scan"), ButtonKind.Prominent, 13, null, () => _m.Scan(true));
        scan.Padding = new Thickness(22, 7, 22, 7);
        scan.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(scan, _compact ? 12 : 18);

        var schedule = ScheduleCard();
        schedule.HorizontalAlignment = HorizontalAlignment.Stretch;
        column.AddSpaced(schedule, _compact ? 12 : 18);
        column.AddSpaced(ScreenshotsCard(), _compact ? 12 : 18);
        if (!_compact && !Host.App.Get<bool>("whatsAppDownloadsEnabled"))
        {
            column.AddSpaced(WaCards.OptIn(), 18);
        }

        // The Settings page has its own full tool for these downloads; the panel gets this one-line home.
        if (_compact && Host.App.Get<bool>("whatsAppDownloadsEnabled"))
        {
            column.AddSpaced(WaCards.Panel(Rebuild), 12);
        }

        if (!FileOps.IsElevated)
        {
            column.AddSpaced(AdminNote(_compact), _compact ? 12 : 18);
        }

        return column;
    }

    private FrameworkElement ScreenshotsCard()
    {
        var days = new[] { 0, 7, 14, 30, 60, 90 }.ToList();
        var stored = CleanerModel.SanitizedScreenshotDays(Host.App.Get<int>("cleanerScreenshotAgeDays"));
        if (!days.Contains(stored))
        {
            days.Add(stored);
            days.Sort();
        }

        var labels = days.Select(d => d == 0 ? "Off" : $"After {d} days").ToList();
        var drop = V.Drop(labels, days.IndexOf(stored), i => Host.App.Set("cleanerScreenshotAgeDays", days[i]), 0, 11.5);
        var label = V.Row(8, UiKit.Icon(G.Camera, 13, ThemeKeys.TextSecondary), UiKit.Text(Loc.T("Forgotten screenshots"), 12, FontWeights.Medium, ThemeKeys.TextPrimary));
        var column = new StackPanel();
        if (_compact)
        {
            column.Children.Add(label);
            drop.HorizontalAlignment = HorizontalAlignment.Left;
            drop.Margin = new Thickness(0, 8, 0, 0);
            column.Children.Add(drop);
        }
        else
        {
            column.Children.Add(V.Split(label, drop));
        }

        if (!_compact)
        {
            var note = V.Secondary(Loc.T("The scan lists the screenshots you have not opened for this long, unchecked, with their total size."), 10);
            note.Margin = new Thickness(0, 8, 0, 0);
            column.Children.Add(note);
        }

        return V.Soft(column, 11, 380);
    }

    private static FrameworkElement AdminNote(bool compact)
    {
        var column = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var infoIcon = UiKit.Icon(G.Info, 13, ThemeKeys.TextSecondary); infoIcon.VerticalAlignment = VerticalAlignment.Top; infoIcon.HorizontalAlignment = HorizontalAlignment.Left; infoIcon.Margin = new Thickness(0, 1, 0, 0); head.Children.Add(infoIcon);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(Loc.T("Run as administrator for a more thorough scan."), 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        var hint = V.Secondary(Loc.T("System caches such as Windows Update downloads, Prefetch and Windows\\Temp are only visible to an administrator. Nothing is changed until you review the list."), 10);
        hint.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(hint);
        var button = V.Mini(Loc.T("Relaunch as administrator"), () =>
        {
            if (Elevated.RelaunchAsAdministrator())
            {
                Host.Services.Shell.Quit();
            }
        });
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 8, 0, 0);
        text.Children.Add(button);
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        column.Children.Add(head);
        return V.Soft(column, compact ? 9 : 11, 380);
    }

    private FrameworkElement ScheduleCard()
    {
        var app = Host.App;
        var freqIndex = new[] { "off", "daily", "weekly" }.ToList().IndexOf(app.Get<string>("cleanerScheduleFrequency"));
        var column = new StackPanel();
        var labelRow = V.Row(8, UiKit.Icon(G.Clock, 13, ThemeKeys.TextSecondary), UiKit.Text(Loc.T("Automatic cleanup"), 12, FontWeights.Medium, ThemeKeys.TextPrimary));

        void OnPref()
        {
            _m.SyncWithPreferences();
            Rebuild();
        }

        if (_compact)
        {
            var open = _scheduleOpen;
            var chevron = UiKit.Icon(open ? G.ChevronDown : G.Chevron, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            chevron.Width = 12;
            var summary = UiKit.Text(new[] { Loc.T("Off"), Loc.T("Daily"), Loc.T("Weekly") }[Math.Max(freqIndex, 0)], 10, FontWeights.Normal, ThemeKeys.TextSecondary);
            var header = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(chevron);
            Grid.SetColumn(labelRow, 1);
            labelRow.Margin = new Thickness(4, 0, 0, 0);
            header.Children.Add(labelRow);
            Grid.SetColumn(summary, 2);
            header.Children.Add(summary);
            header.MouseLeftButtonUp += (_, _) =>
            {
                _scheduleOpen = !_scheduleOpen;
                Rebuild();
            };
            column.Children.Add(header);
            if (!open)
            {
                return V.Soft(column, 9, 380);
            }
        }

        var drop = V.Drop([Loc.T("Off"), Loc.T("Daily"), Loc.T("Weekly")], Math.Max(freqIndex, 0), i =>
        {
            app.Set("cleanerScheduleFrequency", i switch { 1 => "daily", 2 => "weekly", _ => "off" });
            OnPref();
        }, 0, 11.5);
        if (_compact)
        {
            drop.Margin = new Thickness(0, 8, 0, 0);
            drop.HorizontalAlignment = HorizontalAlignment.Left;
            column.Children.Add(drop);
        }
        else
        {
            column.Children.Add(V.Split(labelRow, drop));
        }

        if (freqIndex > 0)
        {
            var details = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (freqIndex == 2)
            {
                var names = CultureInfo.CurrentCulture.DateTimeFormat.DayNames.Select(n => char.ToUpper(n[0]) + n[1..]).ToList();
                var day = V.Drop(names, Math.Clamp(app.Get<int>("cleanerScheduleWeekday") - 1, 0, 6), i => { app.Set("cleanerScheduleWeekday", i + 1); _m.SyncWithPreferences(); Rebuild(); }, 0, 11.5);
                day.Margin = new Thickness(0, 0, 6, 4);
                details.Children.Add(day);
            }

            var twelve = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
            var hour24 = Math.Clamp(app.Get<int>("cleanerScheduleHour"), 0, 23);
            var minute = Math.Clamp(app.Get<int>("cleanerScheduleMinute"), 0, 59);
            if (twelve)
            {
                var (h12, pm) = CleanerModel.Hour12Components(hour24);
                var hours = V.Drop(Enumerable.Range(1, 12).Select(h => h.ToString()).ToList(), h12 - 1, i => { app.Set("cleanerScheduleHour", CleanerModel.Hour24(i + 1, CleanerModel.Hour12Components(Math.Clamp(app.Get<int>("cleanerScheduleHour"), 0, 23)).IsPm)); _m.SyncWithPreferences(); Rebuild(); }, 0, 11.5);
                hours.Margin = new Thickness(0, 0, 4, 4);
                details.Children.Add(hours);
            }
            else
            {
                var hours = V.Drop(Enumerable.Range(0, 24).Select(h => h.ToString()).ToList(), hour24, i => { app.Set("cleanerScheduleHour", i); _m.SyncWithPreferences(); Rebuild(); }, 0, 11.5);
                hours.Margin = new Thickness(0, 0, 4, 4);
                details.Children.Add(hours);
            }

            var minutes = Enumerable.Range(0, 12).Select(i => i * 5).ToList();
            if (!minutes.Contains(minute))
            {
                minutes.Add(minute);
                minutes.Sort();
            }

            var minuteDrop = V.Drop(minutes.Select(mm => $":{mm:00}").ToList(), minutes.IndexOf(minute), i => { app.Set("cleanerScheduleMinute", minutes[i]); _m.SyncWithPreferences(); Rebuild(); }, 0, 11.5);
            minuteDrop.Margin = new Thickness(0, 0, 4, 4);
            details.Children.Add(minuteDrop);
            if (twelve)
            {
                var info = CultureInfo.CurrentCulture.DateTimeFormat;
                var am = V.Drop([info.AMDesignator.Length > 0 ? info.AMDesignator : "AM", info.PMDesignator.Length > 0 ? info.PMDesignator : "PM"], CleanerModel.Hour12Components(hour24).IsPm ? 1 : 0, i =>
                {
                    var current = Math.Clamp(app.Get<int>("cleanerScheduleHour"), 0, 23);
                    app.Set("cleanerScheduleHour", CleanerModel.Hour24(CleanerModel.Hour12Components(current).Hour12, i == 1));
                    _m.SyncWithPreferences();
                    Rebuild();
                }, 0, 11.5);
                am.Margin = new Thickness(0, 0, 0, 4);
                details.Children.Add(am);
            }

            column.Children.Add(details);

            var notify = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var check = new CheckMark(app.Get<bool>("cleanerScheduleNotify"), on =>
            {
                app.Set("cleanerScheduleNotify", on);
                Rebuild();
            });
            notify.Children.Add(check);
            var notifyLabel = UiKit.Text(Loc.T("Notify when done"), 11.5, FontWeights.Normal, ThemeKeys.TextPrimary);
            notifyLabel.Margin = new Thickness(7, 0, 0, 0);
            notifyLabel.VerticalAlignment = VerticalAlignment.Center;
            notify.Children.Add(notifyLabel);
            column.Children.Add(notify);

            if (app.Get<bool>("cleanerScheduleNotify") && ToastsDisabled())
            {
                var warn = V.Orange(Loc.T("WinKnife notifications are turned off in the system."), 10);
                warn.Margin = new Thickness(0, 6, 0, 0);
                column.Children.Add(warn);
                var open = V.Mini(Loc.T("Open Notification Settings…"), () => UiKit.OpenUrl("ms-settings:notifications"), null, 10);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 4, 0, 0);
                column.Children.Add(open);
            }

            if (_m.NextFire is { } next)
            {
                var line = V.Secondary(Loc.F("Next cleanup {0}.", Fmt.DayTime(next)), 10);
                line.Margin = new Thickness(0, 6, 0, 0);
                column.Children.Add(line);
            }

            var last = app.Get<double>("cleanerLastAutoRun");
            if (last > 0)
            {
                var freed = app.Get<int>("cleanerLastAutoFreed");
                var text = freed > 0 ? Loc.F("The last automatic cleanup freed {0}.", Fmt.Bytes(freed)) : Loc.F("Last automatic cleanup {0}.", Fmt.DayTime(Fmt.FromEpoch(last)));
                if (app.Get<int>("cleanerLastAutoFailed") > 0)
                {
                    text += " Some items couldn’t be removed.";
                }

                var lastLine = V.Tertiary(text, 10);
                lastLine.Margin = new Thickness(0, 4, 0, 0);
                column.Children.Add(lastLine);
            }
        }

        var cap = V.Secondary(Loc.T("Cleans only the safe part on its own at the chosen time. Items that must not be lost go to the Recycle Bin."), 10);
        cap.Margin = new Thickness(0, 8, 0, 0);
        column.Children.Add(cap);
        return V.Soft(column, _compact ? 9 : 11, 380);
    }

    private static bool ToastsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
            return key?.GetValue("ToastEnabled") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ busy

    private FrameworkElement Busy(string message, string? detail, bool cancel)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, _compact ? 24 : 70, 0, _compact ? 24 : 70) };
        column.Children.Add(Sparkle(_compact ? 40 : 54, true));
        var text = UiKit.Text(message, 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(text, 16);
        if (!string.IsNullOrEmpty(detail))
        {
            var d = V.Tertiary(detail, 11);
            d.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(d, 6);
        }

        if (cancel)
        {
            var button = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, _compact ? 11 : 13, null, _m.Reset);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(button, 16);
        }

        return column;
    }

    // ------------------------------------------------------------------ results

    private FrameworkElement Results()
    {
        var days = CleanerModel.SanitizedScreenshotDays(Host.App.Get<int>("cleanerScreenshotAgeDays"));
        var root = new StackPanel();
        var padding = _compact ? 12.0 : 16.0;

        var header = new Grid { Margin = new Thickness(padding) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = UiKit.Icon(G.Sparkle, 22, ThemeKeys.TextSecondary);
        glyph.Margin = new Thickness(0, 0, 10, 0);
        header.Children.Add(glyph);
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Text(Loc.T("Cleaner"), 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        titles.Children.Add(UiKit.Text(Loc.F("{0} found", Fmt.Bytes(_m.TotalSize)), 12, FontWeights.Normal, ThemeKeys.TextSecondary));
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = V.IconButton(Glyphs.CloseCircle, Loc.T("Cancel"), _m.Reset, 16, ThemeKeys.TextSecondary, 28);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        root.Children.Add(header);
        root.Children.Add(UiKit.Divider());

        if (_m.Items.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 40) };
            var seal = UiKit.Icon(G.CheckCircle, 34, ThemeKeys.Green);
            seal.FontWeight = FontWeights.Light;
            empty.Children.Add(seal);
            var text = UiKit.Text(Loc.T("Nothing to clean. Your PC is tidy."), 13, FontWeights.Normal, ThemeKeys.TextSecondary);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddSpaced(text, 10);
            root.Children.Add(empty);
            return root;
        }

        var list = new StackPanel { Margin = new Thickness(_compact ? 12 : 16, 10, _compact ? 12 : 16, 10) };
        void Section(string title, params CleanerGroup[] groups)
        {
            var present = groups.Where(g => _m.Items.Any(i => i.Group == g)).ToList();
            if (present.Count == 0)
            {
                return;
            }

            var label = UiKit.Text(_compact ? title : title, _compact ? 11 : 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            label.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 16, 0, 6);
            list.Children.Add(label);
            foreach (var group in present)
            {
                list.Children.Add(GroupRow(group, days));
                if (_compact)
                {
                    list.Children.Add(UiKit.Divider());
                }
            }
        }

        Section(Loc.T("Safe cleanup"), CleanerGroup.LoginItems, CleanerGroup.SafeCaches, CleanerGroup.Logs, CleanerGroup.Developer);
        Section(Loc.T("Optional, review first"), CleanerGroup.Leftovers, CleanerGroup.OtherCaches, CleanerGroup.DeviceBackups, CleanerGroup.Screenshots, CleanerGroup.Trash);
        FrameworkElement body = list;
        if (!_compact)
        {
            body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520, Content = list };
        }

        root.Children.Add(body);
        root.Children.Add(UiKit.Divider());

        var footer = new Grid { Margin = new Thickness(padding) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var count = UiKit.Text(Loc.F("{0} of {1} selected", _m.SelectedCount, _m.Items.Count), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        count.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(count);
        var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, _compact ? 11 : 13, null, _m.Reset);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        var clean = UiKit.Button(Loc.F("Clean {0}", Fmt.Bytes(_m.SelectedSize)), ButtonKind.Prominent, _compact ? 11 : 13, null, () => _m.CleanSelected(true));
        clean.IsEnabled = _m.SelectedCount > 0;
        clean.FontWeight = FontWeights.SemiBold;
        clean.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(clean, 2);
        footer.Children.Add(clean);
        root.Children.Add(footer);
        return root;
    }

    private FrameworkElement GroupRow(CleanerGroup group, int days)
    {
        var items = _m.Items.Where(i => i.Group == group).ToList();
        var (glyph, title, caption) = Meta(group, days);
        var header = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var all = items.Count > 0 && items.All(i => i.Include);
        var check = new CheckMark(all, on => _m.SetGroup(group, on)) { Margin = new Thickness(0, 0, 0, 0) };
        check.VerticalAlignment = VerticalAlignment.Top;
        check.Margin = new Thickness(0, 2, 0, 0);
        header.Children.Add(check);
        var icon = UiKit.Icon(glyph, 14, ThemeKeys.TextSecondary);
        icon.Width = 20;
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(10, 2, 0, 0);
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(UiKit.Text(title, 13, FontWeights.Medium, ThemeKeys.TextPrimary));
        var c = UiKit.Text(caption, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        c.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(c);
        Grid.SetColumn(text, 2);
        header.Children.Add(text);
        var size = UiKit.Text(Fmt.Bytes(items.Sum(i => i.Size)), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        size.VerticalAlignment = VerticalAlignment.Top;
        size.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetColumn(size, 3);
        header.Children.Add(size);

        return V.Disclosure(header, () =>
        {
            var body = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
            if (group == CleanerGroup.Leftovers)
            {
                var note = V.Orange(Loc.T("Found by analysis and left unchecked. Check the path before ticking."), 10);
                note.Margin = new Thickness(46, 0, 0, 6);
                body.Children.Add(note);
            }
            else if (group == CleanerGroup.LoginItems)
            {
                var note = V.Secondary(Loc.T("The entry under Startup apps disappears after you sign out and back in."), 10);
                note.Margin = new Thickness(46, 0, 0, 6);
                body.Children.Add(note);
            }

            foreach (var item in items)
            {
                body.Children.Add(ItemRow(item));
            }

            return body;
        }, Expanded.Contains(group), open =>
        {
            if (open)
            {
                Expanded.Add(group);
            }
            else
            {
                Expanded.Remove(group);
            }
        }, 9);
    }

    private FrameworkElement ItemRow(CleanItem item)
    {
        var grid = new Grid { Margin = new Thickness(8, 1, 0, 1), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var check = new CheckMark(item.Include, on => _m.SetItem(item, on)) { Margin = new Thickness(0, 0, 10, 0) };
        grid.Children.Add(check);
        var text = new StackPanel { Margin = new Thickness(36, 0, 8, 0) };
        text.Children.Add(UiKit.Text(item.Name, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var parent = item.Kind == RemoveKind.EmptyBin || item.Kind == RemoveKind.RegistryValue ? item.Path : (System.IO.Path.GetDirectoryName(item.Path) is { } dir ? Fmt.Home(dir) : item.Path);
        if (item.Group is CleanerGroup.SafeCaches or CleanerGroup.OtherCaches or CleanerGroup.Logs or CleanerGroup.Developer && item.Kind is RemoveKind.Permanent or RemoveKind.PermanentContents or RemoveKind.PermanentFiles)
        {
            parent = Fmt.Home(item.Path);
        }

        var pathText = UiKit.Text(parent, 10, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
        pathText.ToolTip = item.Path;
        pathText.FlowDirection = FlowDirection.LeftToRight;
        text.Children.Add(pathText);
        Grid.SetColumn(text, 0);
        text.Margin = new Thickness(24, 0, 8, 0);
        grid.Children.Add(text);
        Grid.SetColumn(text, 1);
        text.Margin = new Thickness(0, 0, 8, 0);
        check.Margin = new Thickness(0, 0, 10, 0);
        grid.ColumnDefinitions[0].Width = new GridLength(24);
        var size = UiKit.Text(item.Size > 0 ? Fmt.Bytes(item.Size) : string.Empty, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);
        grid.ContextMenu = V.Menu((Loc.T("Reveal in File Explorer"), () => FileOps.Reveal(item.Path), item.Kind != RemoveKind.EmptyBin && item.Kind != RemoveKind.RegistryValue));
        return grid;
    }

    // ------------------------------------------------------------------ done

    private FrameworkElement Done()
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(_compact ? 14 : 28, _compact ? 20 : 60, _compact ? 14 : 28, _compact ? 20 : 60) };
        var check = UiKit.Icon(G.CheckCircle, _compact ? 40 : 54, ThemeKeys.Green);
        column.Children.Add(check);
        var done = UiKit.Text(Loc.T("Done!"), _compact ? 17 : 20, FontWeights.Bold, ThemeKeys.TextPrimary);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(done, 16);
        var freed = UiKit.Text(Loc.F("{0} removed", Fmt.Bytes(_m.Removed)), 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        freed.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(freed, 16);
        var sentences = new List<string>();
        if (_m.RecycledAny)
        {
            sentences.Add(Loc.T("Items went to the Recycle Bin and can be recovered from there."));
        }

        if (_m.DeletedAny)
        {
            sentences.Add("Caches, logs and temporary files were deleted permanently.");
        }

        if (_m.BackupFolder is not null)
        {
            sentences.Add($"Registry entries were saved to {Fmt.Home(_m.BackupFolder)} first.");
        }

        if (sentences.Count > 0)
        {
            var note = V.Tertiary(string.Join(" ", sentences), 10.5);
            note.TextAlignment = TextAlignment.Center;
            note.MaxWidth = 340;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddSpaced(note, 16);
        }

        if (_m.Failed > 0)
        {
            var fail = V.Orange(Loc.T("Some items couldn’t be removed."), 10.5);
            fail.HorizontalAlignment = HorizontalAlignment.Center;
            fail.Margin = new Thickness(40, 0, 40, 0);
            column.AddSpaced(fail, 16);
        }

        var again = UiKit.Button(Loc.T("Scan again"), ButtonKind.Prominent, _compact ? 11 : 13, null, _m.Reset);
        again.Padding = new Thickness(22, 7, 22, 7);
        again.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddSpaced(again, 22);
        return column;
    }
}
