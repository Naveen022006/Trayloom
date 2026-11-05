// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>
/// "Add a Google calendar": the one place a secret iCal address is pasted, in a <see cref="PasswordBox"/> that is cleared whenever the dialog
/// closes. The address goes to <see cref="GoogleCalendarSource.AddAsync"/> and nowhere else, and it is never written to the log.
/// </summary>
internal sealed class GoogleCalendarWindow : Window
{
    private readonly GoogleCalendarSource _source;
    private readonly PasswordBox _box = new();
    private readonly TextBlock _status = UiKit.Text("", 12, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
    private readonly TextBlock _hint = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
    private readonly Button _add;
    private readonly Button _cancel;
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;

    public GoogleCalendarWindow(GoogleCalendarSource source, Window? owner = null)
    {
        _source = source;
        Title = Loc.T("Add a Google calendar");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        this.Themed(BackgroundProperty, ThemeKeys.SettingsWindow);
        this.Themed(ForegroundProperty, ThemeKeys.TextPrimary);
        FontFamily = UiKit.TextFont;
        ThemeManager.BindWindow(this);

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(UiKit.Text(Loc.T("Add a Google calendar"), 16, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        stack.AddSpaced(UiKit.Text(Loc.T("Paste the secret address of a calendar. Trayloom reads it to show your events and never changes them."), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 8);
        stack.AddSpaced(UiKit.Text(Loc.T("1. Open Google Calendar on the web and go to Settings."), 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true), 10);
        stack.AddSpaced(UiKit.Text(Loc.T("2. Under “Settings for my calendars”, pick the calendar, then open “Integrate calendar”."), 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true), 4);
        stack.AddSpaced(UiKit.Text(Loc.T("3. Copy “Secret address in iCal format” and paste it here."), 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true), 4);
        var link = UiKit.Button(Loc.T("Open Google Calendar settings"), ButtonKind.Plain, 12, null, () => UiKit.OpenUrl("https://calendar.google.com/calendar/r/settings"));
        link.HorizontalAlignment = HorizontalAlignment.Left;
        stack.AddSpaced(link, 4);
        AutomationProperties.SetAutomationId(_box, "Calendar.GoogleAddress");
        AutomationProperties.SetName(_box, Loc.T("Secret address in iCal format"));
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddClicked();
                e.Handled = true;
            }
        };
        stack.AddSpaced(_box, 10);
        stack.AddSpaced(UiKit.Text("https://calendar.google.com/calendar/ical/…/basic.ics", 11, FontWeights.Normal, ThemeKeys.TextSecondary), 4);
        stack.AddSpaced(UiKit.Text(Loc.T("The address works like a password: anyone who has it can see this calendar. Trayloom stores it encrypted for your Windows account. You can reset it in Google Calendar at any time."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 10);
        stack.AddSpaced(_status, 8);
        stack.AddSpaced(_hint, 2);

        _cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, 12, null, Close);
        AutomationProperties.SetAutomationId(_cancel, "Calendar.GoogleCancel");
        AutomationProperties.SetName(_cancel, Loc.T("Cancel"));
        _add = UiKit.Button(Loc.T("Add"), ButtonKind.Prominent, 12, null, AddClicked);
        AutomationProperties.SetAutomationId(_add, "Calendar.GoogleAdd");
        AutomationProperties.SetName(_add, Loc.T("Add"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_cancel);
        _add.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(_add);
        stack.AddSpaced(buttons, 14);
        Content = stack;

        // The address must not outlive the dialog on any path (button, title-bar close, Escape), and a check in flight is abandoned.
        Closed += (_, _) =>
        {
            _box.Clear();
            _cts.Cancel();
            _cts.Dispose();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _box.Focus();
    }

    /// <summary>True when a calendar was added.</summary>
    public bool Added { get; private set; }

    private async void AddClicked()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            var address = _box.Password.Trim();
            if (address.Length == 0)
            {
                return;
            }

            SetBusy(true);
            _status.Text = Loc.T("Checking…");
            _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            _hint.Text = "";
            var result = await _source.AddAsync(address, _cts.Token);
            address = "";
            if (result == GoogleAddResult.Added)
            {
                _box.Clear();
                Added = true;
                Close();
                return;
            }

            ShowError(result);
            SetBusy(false);
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed while the check was running.
        }
        catch (Exception)
        {
            // The exception text could carry the address, so only a generic message is shown.
            ShowError(GoogleAddResult.Offline);
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _add.IsEnabled = !busy;
        _box.IsEnabled = !busy;
    }

    private void ShowError(GoogleAddResult result)
    {
        _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
        _hint.Text = "";
        switch (result)
        {
            case GoogleAddResult.InvalidAddress:
                _status.Text = Loc.T("This is not a Google Calendar secret address. It starts with https://calendar.google.com/calendar/ical/ and ends with basic.ics.");
                break;
            case GoogleAddResult.Rejected:
                _status.Text = Loc.T("Google did not accept this address. Copy it again; it changes when you reset it.");
                _hint.Text = Loc.T("If your work account hides the secret address, your administrator has turned it off.");
                break;
            case GoogleAddResult.NotCalendar:
                _status.Text = Loc.T("This address did not return a calendar.");
                break;
            case GoogleAddResult.AlreadyAdded:
                _status.Text = Loc.T("This calendar is already added.");
                break;
            case GoogleAddResult.TooMany:
                _status.Text = Loc.T("You can add up to 10 calendars.");
                break;
            default:
                _status.Text = Loc.T("Could not reach Google. Check your connection and try again.");
                break;
        }
    }
}
