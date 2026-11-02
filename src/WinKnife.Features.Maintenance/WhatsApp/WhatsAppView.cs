// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Maintenance;

/// <summary>The "WhatsApp downloads" tool inside the Cleaner page (Vorssaint's <c>WhatsAppDownloadsSettings</c>).</summary>
internal sealed class WaPageView : ContentControl
{
    private static double _listOffset;
    private readonly WaManager _manager = WaManager.Shared;
    private readonly WaOrganizer _organizer = WaOrganizer.Shared;
    private readonly WaScheduler _scheduler = WaScheduler.Shared;
    private bool _waitingToEnable;
    private bool _organizerTypesOpen;
    private Button? _cleanButton;
    private TextBlock? _countLine;

    public WaPageView()
    {
        Loaded += (_, _) =>
        {
            _manager.Changed += OnManagerChanged;
            _manager.SelectionChanged += OnSelectionChanged;
            _organizer.Changed += OnOtherChanged;
            _scheduler.Changed += OnOtherChanged;
            _manager.SetReviewVisible(true);
            Host.App.Set("whatsAppDownloadsRetentionDays", WaRules.SanitizedRetentionDays(Host.App.Get<int>("whatsAppDownloadsRetentionDays")));
            Host.App.Set("whatsAppOrganizerDelayMinutes", WaRules.SanitizedOrganizerDelayMinutes(Host.App.Get<int>("whatsAppOrganizerDelayMinutes")));
            if (_manager.Phase == WaPhase.Idle)
            {
                _manager.Scan();
            }

            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _manager.Changed -= OnManagerChanged;
            _manager.SelectionChanged -= OnSelectionChanged;
            _organizer.Changed -= OnOtherChanged;
            _scheduler.Changed -= OnOtherChanged;
            _manager.SetReviewVisible(false);
        };
        Rebuild();
    }

    private void OnOtherChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void OnManagerChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_waitingToEnable)
            {
                if (_manager.Phase == WaPhase.Results)
                {
                    _waitingToEnable = false;
                    if (_manager.EligibleCount > 0)
                    {
                        var count = _manager.EligibleCount;
                        Dispatcher.BeginInvoke(() =>
                        {
                            var choice = Dialogs.Choose(WaText.FirstTitle, WaText.FirstMessageFormat(count), [WaText.FutureOnly, WaText.IncludeExisting], WaText.Cancel);
                            if (choice >= 0)
                            {
                                EnableAutomation(choice == 1);
                            }
                            else
                            {
                                Rebuild();
                            }
                        });
                    }
                    else
                    {
                        EnableAutomation(false);
                    }
                }
                else if (_manager.Phase == WaPhase.Failed)
                {
                    _waitingToEnable = false;
                }
            }

            if (_manager.Phase is WaPhase.Results or WaPhase.Failed)
            {
                if (Host.App.Get<bool>("whatsAppDownloadsAutomaticEnabled"))
                {
                    _scheduler.Sync();
                }

                if (Host.App.Get<bool>("whatsAppOrganizerEnabled") && !_organizer.IsBusy && _organizer.NextCheck is null)
                {
                    _organizer.Sync();
                }
            }

            Rebuild();
        });
    }

    private void OnSelectionChanged() => Dispatcher.BeginInvoke(RefreshSelection);

    private void RefreshSelection()
    {
        if (_cleanButton is not null)
        {
            _cleanButton.Content = WaText.CleanSelectedFormat(_manager.SelectedCount, Fmt.Bytes(_manager.SelectedBytes));
            _cleanButton.IsEnabled = _manager.SelectedCount > 0;
        }
    }

    private void EnableAutomation(bool includeExisting)
    {
        var app = Host.App;
        app.Set("whatsAppDownloadsIncludeExisting", includeExisting);
        app.Set("whatsAppDownloadsAutomaticStartDate", Fmt.Epoch());
        app.Set("whatsAppDownloadsAutomaticEnabled", true);
        _scheduler.Sync();
        _manager.Scan();
        Rebuild();
    }

    private void RefreshResultsIfVisible()
    {
        if (_manager.Phase is WaPhase.Results or WaPhase.Done)
        {
            _manager.Scan();
        }
    }

    // ------------------------------------------------------------------ layout

    private void Rebuild()
    {
        var offset = FindScroll()?.VerticalOffset;
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        void Add(UIElement e) => root.AddSpaced(e, root.Children.Count == 0 ? 0 : 16);
        Add(IntroCard());
        Add(AutomationCard());
        Add(TypesCard());
        Add(OrganizerCard());
        Add(ManualCard());
        Add(ActivityCard());
        Add(PrivacyCard());
        Content = root;
        if (offset is > 0)
        {
            Dispatcher.BeginInvoke(() => FindScroll()?.ScrollToVerticalOffset(offset.Value), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private ScrollViewer? FindScroll()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer sv && sv.ComputedVerticalScrollBarVisibility != Visibility.Collapsed)
            {
                return sv;
            }
        }

        return null;
    }

    private static MacSwitch Switch(bool on, Action<bool> toggled) => new(on, SwitchSize.Small, toggled);

    private static UIElement SwitchRow(string title, bool on, Action<bool> toggled, bool enabled = true)
    {
        var sw = Switch(on, toggled);
        sw.IsEnabled = enabled;
        return new SettingsRow(null, title, null, sw);
    }

    private static TextBlock Caption(string text, string brush = ThemeKeys.TextSecondary, double size = 11.5)
    {
        return UiKit.Text(text, size, FontWeights.Normal, brush, wrap: true);
    }

    /// <summary>A glyph followed by wrapping text (the icon sits at the first line).</summary>
    private static UIElement IconLine(string glyph, string text, double size, string glyphBrush, string textBrush)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = UiKit.Icon(glyph, size + 1, glyphBrush);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 0, 0);
        grid.Children.Add(icon);
        var label = UiKit.Text(text, size, FontWeights.Normal, textBrush, wrap: true);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        return grid;
    }

    private static string DateText(DateTime date) => Fmt.DayTime(date);

    // ------------------------------------------------------------------ 1. introduction

    private UIElement IntroCard()
    {
        var card = new SettingsCard();
        card.Add(SwitchRow(WaText.Title, true, on =>
        {
            if (!on)
            {
                Host.App.Set("whatsAppDownloadsEnabled", false);
            }
        }));
        card.Add(Caption(Host.App.Get<bool>("whatsAppOrganizerEnabled") ? WaText.OrgPrivacyNote : WaText.Intro, ThemeKeys.TextPrimary, 12));

        var path = _manager.DownloadsPath;
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(G.Folder, 14, ThemeKeys.TextSecondary);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 8, 0);
        folderRow.Children.Add(icon);
        var texts = new StackPanel();
        texts.Children.Add(UiKit.Text(WaText.Folder, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        var pathText = UiKit.Text(Fmt.Home(path), 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        pathText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        pathText.ToolTip = path;
        texts.Children.Add(pathText);
        Grid.SetColumn(texts, 1);
        folderRow.Children.Add(texts);
        var reveal = V.Mini(WaText.ShowInExplorer, () => FileOps.OpenFolder(path));
        reveal.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(reveal, 2);
        folderRow.Children.Add(reveal);
        card.Add(folderRow);

        switch (_manager.Access)
        {
            case WaAccess.Available:
                card.Add(IconLine(G.CheckCircle, WaText.AccessReady, 11, ThemeKeys.Green, ThemeKeys.Green));
                break;
            case WaAccess.Denied:
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(IconLine(G.Warning, WaText.AccessDenied, 11, ThemeKeys.Orange, ThemeKeys.Orange));
                var open = V.Mini(WaText.OpenWindowsSecurity, () => UiKit.OpenUrl("windowsdefender://ransomwareprotection"));
                open.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(open, 1);
                row.Children.Add(open);
                card.Add(row);
                break;
        }

        return card;
    }

    // ------------------------------------------------------------------ 2. automation

    private UIElement AutomationCard()
    {
        var app = Host.App;
        var card = new SettingsCard();
        var automatic = app.Get<bool>("whatsAppDownloadsAutomaticEnabled");
        card.Add(SwitchRow(WaText.Automatic, automatic || _waitingToEnable, on =>
        {
            if (on)
            {
                _waitingToEnable = true;
                _manager.Scan();
            }
            else
            {
                _waitingToEnable = false;
                app.Set("whatsAppDownloadsAutomaticEnabled", false);
                _scheduler.Sync();
                Rebuild();
            }
        }));
        card.Add(Caption(WaText.AutomaticCaption));

        var days = WaRules.AllowedRetentionDays.Select(d => (d, WaText.DaysFormat(d))).ToList();
        card.Add(new PickerRow<int>(null, WaText.Retention, null, days, WaRules.SanitizedRetentionDays(app.Get<int>("whatsAppDownloadsRetentionDays")), value =>
        {
            app.Set("whatsAppDownloadsRetentionDays", WaRules.SanitizedRetentionDays(value));
            RefreshResultsIfVisible();
        }, 120));
        card.Add(Caption(WaText.RetentionCaption));

        var notify = app.Get<bool>("whatsAppDownloadsNotify");
        card.Add(SwitchRow(WaText.NotifyWhenDone, notify, on =>
        {
            app.Set("whatsAppDownloadsNotify", on);
            Rebuild();
        }));
        if (notify && ToastsDisabled())
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Caption(WaText.NotificationsOff, ThemeKeys.Orange, 11));
            var open = V.Mini(WaText.OpenNotificationSettings, () => UiKit.OpenUrl("ms-settings:notifications"), null, 10);
            open.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            card.Add(row);
        }

        return card;
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

    // ------------------------------------------------------------------ 3. file types

    private static string CategoryName(WaCategory c) => c switch
    {
        WaCategory.Image => WaText.Image,
        WaCategory.Video => WaText.Video,
        WaCategory.Audio => WaText.Audio,
        WaCategory.Document => WaText.Document,
        WaCategory.Archive => WaText.Archive,
        _ => WaText.Other,
    };

    private static string CategoryGlyph(WaCategory c) => c switch
    {
        WaCategory.Image => G.Photo,
        WaCategory.Video => "",
        WaCategory.Audio => "",
        WaCategory.Document => G.Document,
        WaCategory.Archive => G.Package,
        _ => "",
    };

    private static UIElement CheckLabel(string text, bool on, Action<bool> toggled)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
        var check = new CheckMark(on, toggled);
        row.Children.Add(check);
        var label = UiKit.Text(text, 12, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(8, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Cursor = System.Windows.Input.Cursors.Hand;
        label.MouseLeftButtonUp += (_, _) =>
        {
            var next = check.IsChecked != true;
            check.IsChecked = next;
            toggled(next);
        };
        row.Children.Add(label);
        return row;
    }

    private UIElement TypesGrid(string key, Action afterChange)
    {
        var enabled = WaRules.DecodedCategories(Host.App.Get<string>(key));
        var panel = new StackPanel();
        panel.Children.Add(CheckLabel(WaText.AllTypes, enabled.Count == WaRules.AllCategories.Length, all =>
        {
            Host.App.Set(key, WaRules.EncodedCategories(all ? WaRules.AllCategories : []));
            afterChange();
            Rebuild();
        }));
        for (var i = 0; i < WaRules.AllCategories.Length; i += 2)
        {
            var grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var j = 0; j < 2; j++)
            {
                var category = WaRules.AllCategories[i + j];
                var item = CheckLabel(CategoryName(category), enabled.Contains(category), on =>
                {
                    var set = WaRules.DecodedCategories(Host.App.Get<string>(key));
                    if (on)
                    {
                        set.Add(category);
                    }
                    else
                    {
                        set.Remove(category);
                    }

                    Host.App.Set(key, WaRules.EncodedCategories(set));
                    afterChange();
                    Rebuild();
                });
                Grid.SetColumn(item, j * 2);
                grid.Children.Add(item);
            }

            panel.Children.Add(grid);
        }

        return panel;
    }

    private UIElement TypesCard()
    {
        var card = new SettingsCard(WaText.FileTypes);
        card.Add(TypesGrid("whatsAppDownloadsCategories", RefreshResultsIfVisible));
        return card;
    }

    // ------------------------------------------------------------------ 4. organizer

    private UIElement OrganizerCard()
    {
        var app = Host.App;
        var card = new SettingsCard();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(UiKit.Text(WaText.OrgTitle, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var badge = new BadgePill(WaText.OrgExperimental, BadgeStyle.Orange) { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(badge);
        card.Add(titleRow);
        card.Add(Caption(WaText.OrgDescription));
        var enabled = app.Get<bool>("whatsAppOrganizerEnabled");
        card.Add(SwitchRow(WaText.OrgEnabled, enabled, on =>
        {
            app.Set("whatsAppOrganizerEnabled", on);
            if (on)
            {
                _manager.Scan();
            }

            _organizer.Sync();
            Rebuild();
        }, !_organizer.IsBusy));
        if (!enabled)
        {
            return card;
        }

        card.Add(Caption(WaText.OrgEnabledCaption, ThemeKeys.Orange));
        card.Add(DestinationRow());

        var busy = _organizer.IsBusy;
        var layouts = new (string Value, string Label)[] { ("flat", WaText.OrgFlat), ("category", WaText.OrgByType), ("month", WaText.OrgByMonth) };
        card.Add(Disabled(new PickerRow<string>(null, WaText.OrgOrganization, null, layouts, WaRules.Id(WaRules.ParseLayout(app.Get<string>("whatsAppOrganizerLayout"))), value =>
        {
            app.Set("whatsAppOrganizerLayout", value);
            _organizer.Sync();
        }, 150), busy));

        var delays = WaRules.AllowedOrganizerDelayMinutes.Select(m => (m, WaText.MinutesFormat(m))).ToList();
        card.Add(Disabled(new PickerRow<int>(null, WaText.OrgDelay, null, delays, WaRules.SanitizedOrganizerDelayMinutes(app.Get<int>("whatsAppOrganizerDelayMinutes")), value =>
        {
            app.Set("whatsAppOrganizerDelayMinutes", WaRules.SanitizedOrganizerDelayMinutes(value));
            _organizer.Sync();
        }, 120), busy));

        var actions = new (string Value, string Label)[]
        {
            ("trashNew", WaText.OrgTrashDuplicate),
            ("keepBoth", WaText.OrgKeepBoth),
            ("replaceExisting", WaText.OrgReplaceExisting),
        };
        card.Add(Disabled(new PickerRow<string>(null, WaText.OrgDuplicateAction, null, actions, WaRules.Id(WaRules.ParseDuplicateAction(app.Get<string>("whatsAppOrganizerDuplicateAction"))), value =>
        {
            app.Set("whatsAppOrganizerDuplicateAction", value);
            _organizer.Sync();
        }, 230), busy));
        card.Add(Caption(WaText.OrgDuplicateCaption));

        var types = new StackPanel();
        var head = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(UiKit.Text(WaText.FileTypes, 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        var chevron = UiKit.Icon(_organizerTypesOpen ? G.ChevronDown : G.Chevron, 10, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        Grid.SetColumn(chevron, 1);
        head.Children.Add(chevron);
        head.MouseLeftButtonUp += (_, _) =>
        {
            _organizerTypesOpen = !_organizerTypesOpen;
            Rebuild();
        };
        types.Children.Add(head);
        if (_organizerTypesOpen)
        {
            var grid = TypesGrid("whatsAppOrganizerCategories", _organizer.Sync);
            ((FrameworkElement)grid).Margin = new Thickness(25, 10, 0, 0);
            types.Children.Add(grid);
        }

        card.Add(Disabled(types, busy));

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var now = UiKit.Button(WaText.OrgOrganizeNow, ButtonKind.Prominent, 12, null, _organizer.RunNow);
        now.IsEnabled = !busy;
        buttons.Children.Add(now);
        if (busy)
        {
            var spinner = new Spinner(14) { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(spinner, 1);
            buttons.Children.Add(spinner);
        }

        var undo = UiKit.Button(WaText.OrgUndo, ButtonKind.Bordered, 12, null, () => _organizer.UndoLast());
        undo.IsEnabled = _organizer.CanUndo && !busy;
        Grid.SetColumn(undo, 3);
        buttons.Children.Add(undo);
        card.Add(buttons);

        card.Add(OrganizerStatus());
        card.Add(Caption(WaText.OrgPrivacyNote, ThemeKeys.TextSecondary, 10.5));
        return card;
    }

    private static UIElement Disabled(UIElement element, bool disabled)
    {
        element.IsEnabled = !disabled;
        return element;
    }

    private UIElement DestinationRow()
    {
        var app = Host.App;
        var column = new StackPanel();
        column.Children.Add(UiKit.Text(WaText.OrgDestination, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(G.Folder, 13, ThemeKeys.TextSecondary);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        var destination = WaOrganizer.DestinationPath();
        var path = UiKit.Text(Fmt.Home(destination), 11, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        path.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        path.ToolTip = destination;
        path.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(path, 1);
        row.Children.Add(path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var choose = V.Mini(WaText.OrgChooseFolder, () =>
        {
            var picked = Dialogs.PickFolder(WaText.OrgDestination, destination);
            if (picked is null)
            {
                return;
            }

            if (!_organizer.SetDestination(picked))
            {
                Dialogs.Alert(WaText.OrgInvalidDestination, string.Empty, WaText.Ok);
            }

            Rebuild();
        });
        choose.IsEnabled = !_organizer.IsBusy;
        choose.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(choose);
        if (!string.IsNullOrEmpty(app.Get<string>("whatsAppOrganizerDestinationPath")))
        {
            var reset = V.Mini(WaText.OrgUseDefault, () =>
            {
                _organizer.SetDestination(null);
                Rebuild();
            });
            reset.IsEnabled = !_organizer.IsBusy;
            reset.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(reset);
        }

        var go = V.IconButton(G.Chevron, WaText.ShowInExplorer, () => FileOps.OpenFolder(destination), 11, ThemeKeys.TextSecondary, 24);
        go.Margin = new Thickness(4, 0, 0, 0);
        buttons.Children.Add(go);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);
        column.Children.Add(row);
        return column;
    }

    private UIElement OrganizerStatus()
    {
        var app = Host.App;
        switch (_organizer.Phase)
        {
            case WaOrganizerPhase.Organizing:
            case WaOrganizerPhase.Undoing:
                return Caption(WaText.OrgWorking, ThemeKeys.TextSecondary, 11);
            case WaOrganizerPhase.Done:
                return Caption(WaText.OrgResultFormat(_organizer.DoneMoved, _organizer.DoneDuplicates, _organizer.DoneFailed), _organizer.DoneFailed == 0 ? ThemeKeys.Green : ThemeKeys.Orange, 11);
            case WaOrganizerPhase.Failed:
                return Caption(WaText.OrgResultFormat(0, 0, 1), ThemeKeys.Orange, 11);
            default:
                var last = app.Get<double>("whatsAppOrganizerLastRun");
                return last > 0
                    ? Caption(WaText.OrgLastRunFormat(DateText(Fmt.FromEpoch(last)), app.Get<int>("whatsAppOrganizerLastMoved"), app.Get<int>("whatsAppOrganizerLastDuplicates"), app.Get<int>("whatsAppOrganizerLastFailed")), ThemeKeys.TextSecondary, 11)
                    : Caption(WaText.OrgNeverRun, ThemeKeys.TextSecondary, 11);
        }
    }

    // ------------------------------------------------------------------ 5. clean now

    private UIElement ManualCard()
    {
        var card = new SettingsCard(WaText.ManualTitle);
        card.Add(Caption(WaText.ManualIntro));
        var scanRow = new StackPanel { Orientation = Orientation.Horizontal };
        var scan = UiKit.Button(WaText.Scan, ButtonKind.Prominent, 12, G.Search, _manager.Scan);
        scan.IsEnabled = _manager.Phase is not (WaPhase.Scanning or WaPhase.Cleaning);
        scanRow.Children.Add(scan);
        if (_manager.Phase == WaPhase.Scanning)
        {
            var spinner = new Spinner(14) { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            scanRow.Children.Add(spinner);
            var text = Caption(WaText.Scanning, ThemeKeys.TextSecondary, 11);
            text.Margin = new Thickness(8, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            scanRow.Children.Add(text);
        }

        card.Add(scanRow);
        _cleanButton = null;
        _countLine = null;
        switch (_manager.Phase)
        {
            case WaPhase.Results:
                ResultsView(card);
                break;
            case WaPhase.Cleaning:
                var busy = new StackPanel { Orientation = Orientation.Horizontal };
                busy.Children.Add(new Spinner(14) { VerticalAlignment = VerticalAlignment.Center });
                var cleaning = Caption(WaText.Cleaning, ThemeKeys.TextSecondary, 12);
                cleaning.Margin = new Thickness(8, 0, 0, 0);
                busy.Children.Add(cleaning);
                card.Add(busy);
                break;
            case WaPhase.Done:
                var ok = _manager.DoneFailed == 0;
                var done = new StackPanel { Orientation = Orientation.Horizontal };
                var glyph = UiKit.Icon(ok ? G.CheckCircle : G.Warning, 14, ok ? ThemeKeys.Green : ThemeKeys.Orange);
                glyph.VerticalAlignment = VerticalAlignment.Center;
                done.Children.Add(glyph);
                var line = Caption(WaText.NotificationFormat(_manager.DoneMoved, Fmt.Bytes(_manager.DoneBytes), _manager.DoneFailed), ThemeKeys.TextPrimary, 11.5);
                line.Margin = new Thickness(9, 0, 0, 0);
                line.VerticalAlignment = VerticalAlignment.Center;
                done.Children.Add(line);
                card.Add(done);
                break;
            case WaPhase.Failed:
                card.Add(Caption(WaText.ScanFailed, ThemeKeys.Orange, 11.5));
                break;
        }

        return card;
    }

    private void ResultsView(SettingsCard card)
    {
        if (_manager.Candidates.Count == 0)
        {
            card.Add(IconLine(G.CheckCircle, WaText.NoFiles, 12, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary));
            return;
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _countLine = Caption(WaText.ResultsFormat(_manager.Candidates.Count, Fmt.Bytes(_manager.TotalBytes)), ThemeKeys.TextSecondary, 11.5);
        _countLine.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_countLine);
        var rules = V.Mini(WaText.SelectRules, _manager.SelectRules);
        Grid.SetColumn(rules, 1);
        header.Children.Add(rules);
        card.Add(header);

        var list = new StackPanel();
        for (var i = 0; i < _manager.Candidates.Count; i++)
        {
            list.Children.Add(CandidateRow(_manager.Candidates[i]));
            if (i < _manager.Candidates.Count - 1)
            {
                list.Children.Add(UiKit.Divider());
            }
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 320, Content = list };
        scroll.ScrollChanged += (_, _) => _listOffset = scroll.VerticalOffset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(_listOffset);
        var frame = new Border { CornerRadius = new CornerRadius(8), Child = scroll };
        frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        card.Add(frame);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var note = Caption(WaText.TrashNote, ThemeKeys.TextTertiary, 10);
        note.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(note);
        _cleanButton = UiKit.Button(WaText.CleanSelectedFormat(_manager.SelectedCount, Fmt.Bytes(_manager.SelectedBytes)), ButtonKind.Prominent, 12, null, () => _manager.CleanSelected(false));
        _cleanButton.IsEnabled = _manager.SelectedCount > 0;
        _cleanButton.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_cleanButton, 1);
        footer.Children.Add(_cleanButton);
        card.Add(footer);
    }

    private FrameworkElement CandidateRow(WaCandidate candidate)
    {
        var grid = new Grid { Margin = new Thickness(9, 6, 9, 6), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 30 });
        var check = new CheckMark(candidate.Include, on => _manager.SetInclude(on, candidate.Id)) { IsEnabled = !candidate.Excluded, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(check);
        var icon = UiKit.Icon(CategoryGlyph(candidate.Category), 13, candidate.Excluded ? ThemeKeys.TextSecondary : ThemeKeys.Accent);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(UiKit.Text(candidate.Name, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text($"{CategoryName(candidate.Category)} · {Fmt.ShortDateTime(candidate.DownloadedAt)}", 10, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis));
        ((TextBlock)text.Children[0]).ToolTip = candidate.Path;
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var size = UiKit.Text(Fmt.Bytes(candidate.Size), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        size.VerticalAlignment = VerticalAlignment.Center;
        size.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(size, 3);
        grid.Children.Add(size);
        FrameworkElement trailing;
        if (candidate.Excluded)
        {
            trailing = V.Mini(WaText.ManageAgain, () => _manager.RemoveExclusion(candidate.Id), null, 9.5);
        }
        else
        {
            var more = V.IconButton(G.More, null, () => { }, 13, ThemeKeys.TextSecondary, 24);
            var menu = V.Menu((WaText.Keep, () => _manager.Exclude(candidate.Id), true), (WaText.ShowInExplorer, () => _manager.Reveal(candidate.Id), true));
            more.Click += (_, _) =>
            {
                menu.PlacementTarget = more;
                menu.IsOpen = true;
            };
            trailing = more;
        }

        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 4);
        grid.Children.Add(trailing);
        grid.ContextMenu = candidate.Excluded
            ? V.Menu((WaText.ManageAgain, () => _manager.RemoveExclusion(candidate.Id), true), (WaText.ShowInExplorer, () => _manager.Reveal(candidate.Id), true))
            : V.Menu((WaText.Keep, () => _manager.Exclude(candidate.Id), true), (WaText.ShowInExplorer, () => _manager.Reveal(candidate.Id), true));
        return grid;
    }

    // ------------------------------------------------------------------ 6/7. activity and privacy

    private UIElement ActivityCard()
    {
        var app = Host.App;
        var card = new SettingsCard(WaText.Activity);
        var last = app.Get<double>("whatsAppDownloadsLastCleanup");
        card.Add(last > 0
            ? Caption(WaText.LastRunFormat(DateText(Fmt.FromEpoch(last)), app.Get<int>("whatsAppDownloadsLastCleanupCount"), Fmt.Bytes(app.Get<long>("whatsAppDownloadsLastCleanupBytes")), app.Get<int>("whatsAppDownloadsLastCleanupFailed")), ThemeKeys.TextPrimary, 11.5)
            : Caption(WaText.NeverRun, ThemeKeys.TextSecondary, 11.5));
        if (app.Get<bool>("whatsAppDownloadsAutomaticEnabled") && _scheduler.NextFire is { } next)
        {
            card.Add(Caption(WaText.NextRunFormat(DateText(next)), ThemeKeys.TextSecondary, 11.5));
        }

        return card;
    }

    private UIElement PrivacyCard()
    {
        var card = new SettingsCard();
        card.Add(IconLine(G.Shield, Host.App.Get<bool>("whatsAppOrganizerEnabled") ? WaText.OrgPrivacyNote : WaText.LocalNote, 11.5, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary));
        card.Add(Caption(WaText.TrashNote));
        return card;
    }
}

/// <summary>The WhatsApp cards of the Cleaner's idle state: the opt-in card (Settings) and the one-line home (panel).</summary>
internal static class WaCards
{
    private static bool _panelOpen;

    /// <summary>One-shot request from the panel's "Manage…" button: open the Cleaner page on the WhatsApp tool.</summary>
    public static bool PendingWhatsAppTool { get; set; }

    /// <summary>Raised when the tool should be shown by an already open Cleaner page.</summary>
    public static event Action? ToolRequested;

    public static void RequestTool()
    {
        PendingWhatsAppTool = true;
        ToolRequested?.Invoke();
    }

    /// <summary>Opt-in switch shown on the Settings page while the tool is off.</summary>
    public static FrameworkElement OptIn()
    {
        var column = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(WaText.Title, 12, FontWeights.Medium, ThemeKeys.TextPrimary));
        var sw = new MacSwitch(false, SwitchSize.Mini, on => Host.App.Set("whatsAppDownloadsEnabled", on));
        Grid.SetColumn(sw, 1);
        row.Children.Add(sw);
        column.Children.Add(row);
        var caption = V.Secondary(WaText.HubDescription, 10);
        caption.Margin = new Thickness(0, 6, 0, 0);
        column.Children.Add(caption);
        return V.Soft(column, 11, 380);
    }

    /// <summary>The panel's one-line home: state at a glance, last and next pass when expanded, a jump into the page.</summary>
    public static FrameworkElement Panel(Action rebuild)
    {
        var app = Host.App;
        var automatic = app.Get<bool>("whatsAppDownloadsAutomaticEnabled");
        var next = WaScheduler.Shared.NextFire;
        var summary = !automatic ? WaText.Off : next is { } n ? Fmt.DayTime(n) : WaText.Automatic;
        var column = new StackPanel();
        var header = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = UiKit.Icon(_panelOpen ? G.ChevronDown : G.Chevron, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.Width = 12;
        header.Children.Add(chevron);
        var label = V.Row(8, UiKit.Icon(G.Download, 13, ThemeKeys.TextSecondary), UiKit.Text(WaText.Title, 12, FontWeights.Medium, ThemeKeys.TextPrimary));
        label.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(label, 1);
        header.Children.Add(label);
        var state = UiKit.Text(summary, 10, FontWeights.Normal, ThemeKeys.TextSecondary);
        Grid.SetColumn(state, 2);
        header.Children.Add(state);
        header.MouseLeftButtonUp += (_, _) =>
        {
            _panelOpen = !_panelOpen;
            rebuild();
        };
        column.Children.Add(header);
        if (_panelOpen)
        {
            var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var last = app.Get<double>("whatsAppDownloadsLastCleanup");
            body.Children.Add(last > 0
                ? V.Secondary(WaText.LastRunFormat(Fmt.DayTime(Fmt.FromEpoch(last)), app.Get<int>("whatsAppDownloadsLastCleanupCount"), Fmt.Bytes(app.Get<long>("whatsAppDownloadsLastCleanupBytes")), app.Get<int>("whatsAppDownloadsLastCleanupFailed")), 10)
                : V.Secondary(WaText.NeverRun, 10));
            if (automatic && next is { } when2)
            {
                var line = V.Tertiary(WaText.NextRunFormat(Fmt.DayTime(when2)), 10);
                line.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(line);
            }

            var manage = V.Mini(WaText.ManageButton, () =>
            {
                RequestTool();
                Host.OpenSettings(SettingsPages.Cleaner);
            });
            manage.HorizontalAlignment = HorizontalAlignment.Left;
            manage.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(manage);
            column.Children.Add(body);
        }

        return V.Soft(column, 11, 380);
    }
}

/// <summary>Applies preference changes of the WhatsApp tool to its scheduler and organizer.</summary>
internal static class WaHub
{
    public static void Start()
    {
        WaScheduler.Shared.Sync();
        WaOrganizer.Shared.Sync();
    }

    public static void Stop()
    {
        WaScheduler.Shared.Stop();
        WaOrganizer.Shared.Stop();
        WaManager.Shared.Reset();
    }

    /// <summary>Called for every changed flat key (any thread).</summary>
    public static void OnSetting(string key)
    {
        switch (key)
        {
            case "whatsAppDownloadsEnabled":
                V.Post(() =>
                {
                    WaScheduler.Shared.Sync();
                    WaOrganizer.Shared.Sync();
                    if (!Host.App.Get<bool>("whatsAppDownloadsEnabled") || !Host.Runtime.IsAvailable("cleaner"))
                    {
                        WaManager.Shared.Reset();
                        WaOrganizer.Shared.Stop();
                    }
                });
                break;
            case "whatsAppDownloadsAutomaticEnabled":
            case "whatsAppDownloadsAccessConfirmed":
                V.Post(WaScheduler.Shared.Sync);
                if (key == "whatsAppDownloadsAccessConfirmed")
                {
                    V.Post(WaOrganizer.Shared.Sync);
                }

                break;
            case "whatsAppOrganizerEnabled":
            case "whatsAppOrganizerDestinationPath":
            case "whatsAppOrganizerDelayMinutes":
            case "whatsAppOrganizerCategories":
            case "whatsAppOrganizerLayout":
            case "whatsAppOrganizerDuplicateAction":
                V.Post(WaOrganizer.Shared.Sync);
                break;
        }
    }
}
