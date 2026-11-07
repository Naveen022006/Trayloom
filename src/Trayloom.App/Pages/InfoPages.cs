// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>The About page (spec 02e section 9): identity block and the Updates section.</summary>
internal static class AboutPage
{
    public static FrameworkElement Build(ShellServices s)
    {
        var stack = new StackPanel();

        // Identity block.
        var identity = new SettingsCard();
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        var badge = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(76 * 0.26),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0x3A, 0x3A, 0x3E), 0),
                new GradientStop(Color.FromRgb(0x16, 0x16, 0x18), 0.5),
                new GradientStop(Colors.Black, 1),
            ], new Point(0, 0), new Point(1, 1)),
            Child = BrandMark.Create(54, ThemeKeys.OnAccent),
        };
        col.Children.Add(badge);
        var name = UiKit.Text(AppInfo.Name, 18, FontWeights.Bold, ThemeKeys.TextPrimary);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 14, 0, 0);
        col.Children.Add(name);
        var versionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        versionRow.Children.Add(UiKit.Text("Version " + AppInfo.Version, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        if (AppInfo.IsBeta)
        {
            var beta = new BadgePill(Loc.T("Beta"), BadgeStyle.Orange) { Margin = new Thickness(6, 0, 0, 0) };
            versionRow.Children.Add(beta);
        }

        col.Children.Add(versionRow);
        var desc = UiKit.Text(Loc.T("A utility hub for your PC.\nEnergy, system monitor, scrolling and a window switcher, right in the system tray."), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        desc.TextAlignment = TextAlignment.Center;
        desc.HorizontalAlignment = HorizontalAlignment.Center;
        desc.Margin = new Thickness(0, 14, 0, 0);
        col.Children.Add(desc);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(UiKit.Button(Loc.T("Review introduction"), ButtonKind.Bordered, 13, onClick: () => s.Shell.ShowOnboarding()));
        var highlights = UiKit.Button(Loc.T("Review highlights"), ButtonKind.Bordered, 13, onClick: () => s.Shell.OpenSettings(SettingsPages.WhatsNew));
        highlights.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(highlights);
        var link = UiKit.Button(Loc.T("View on GitHub"), ButtonKind.Plain, 13, onClick: () => UiKit.OpenUrl(AppInfo.RepositoryUrl));
        link.Margin = new Thickness(10, 0, 0, 0);
        link.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Accent);
        buttons.Children.Add(link);
        col.Children.Add(buttons);
        var help = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        help.Children.Add(UiKit.Button(Loc.T("Report a problem"), ButtonKind.Bordered, 13, onClick: () => s.Shell.ReportProblem()));
        if (!InstallManager.IsInstalledCopy && !InstallManager.IsDevelopmentBuild)
        {
            var install = UiKit.Button(Loc.T("Install Trayloom…"), ButtonKind.Bordered, 13, onClick: () => s.Shell.ShowInstallOffer());
            install.Margin = new Thickness(10, 0, 0, 0);
            help.Children.Add(install);
        }

        col.Children.Add(help);
        var sac = SmartAppControl.GetState();
        if (sac is SmartAppControlState.Enforcing or SmartAppControlState.Evaluation)
        {
            var note = UiKit.Text(sac == SmartAppControlState.Enforcing
                ? Loc.T("Smart App Control is on. It may slow the first start or block unsigned parts of Trayloom; Trayloom explains it if that happens.")
                : Loc.T("Smart App Control is in evaluation mode. It may slow the first start."), 11, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
            note.TextAlignment = TextAlignment.Center;
            note.MaxWidth = 420;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.Margin = new Thickness(0, 12, 0, 0);
            col.Children.Add(note);
        }

        var copy =UiKit.Text(AppInfo.Copyright, 11, FontWeights.Normal, ThemeKeys.TextTertiary);
        copy.HorizontalAlignment = HorizontalAlignment.Center;
        copy.Margin = new Thickness(0, 14, 0, 0);
        col.Children.Add(copy);
        identity.Add(col);
        stack.AddSpaced(identity, 20);

        // Updates. Notify only: Trayloom asks GitHub about a newer version and links to the release page.
        var updates = new SettingsCard(Loc.T("Updates"));
        updates.Add(Toggle(s, AppKeys.AutoCheckUpdates, Loc.T("Check for updates automatically")));
        updates.Add(Toggle(s, AppKeys.IncludeBetaUpdates, Loc.T("Receive beta updates")));
        updates.AddCaption(Loc.T("Beta versions include features in development and may contain bugs or incomplete behavior."));
        updates.AddCaption(Loc.T("Trayloom asks GitHub about once a day whether a newer version exists. Nothing is downloaded or installed; you get a link to the release page."));
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        var checkButton = UiKit.Button(Loc.T("Check now"), ButtonKind.Bordered, 13);
        var lastChecked = new TextBlock();
        void Refresh()
        {
            status.Children.Clear();
            var service = s.Updates;
            if (service is null)
            {
                return;
            }

            checkButton.IsEnabled = service.State != UpdateState.Checking;
            switch (service.State)
            {
                case UpdateState.Checking:
                    status.Children.Add(UiKit.Text(Loc.T("Checking…"), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary));
                    break;
                case UpdateState.UpToDate:
                    status.Children.Add(UiKit.Icon("\uE930", 13, ThemeKeys.Green));
                    var ok = UiKit.Text(Loc.T("You’re on the latest version."), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary);
                    ok.Margin = new Thickness(6, 0, 0, 0);
                    status.Children.Add(ok);
                    break;
                case UpdateState.Available:
                    status.Children.Add(UiKit.Text(Loc.F("Update available: {0}", service.LatestVersion), 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
                    var url = service.ReleaseUrl;
                    var view = UiKit.Button(Loc.T("View release"), ButtonKind.Bordered, 13, onClick: () =>
                    {
                        if (url is not null)
                        {
                            UiKit.OpenUrl(url);
                        }
                    });
                    view.Margin = new Thickness(10, 0, 0, 0);
                    status.Children.Add(view);
                    break;
                case UpdateState.Failed:
                    status.Children.Add(UiKit.Text(Loc.F("Couldn’t check: {0}", service.FailureText ?? string.Empty), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary));
                    break;
            }

            lastChecked.Text = service.LastCheckedUtc is { } when
                ? Loc.F("Last checked: {0}", when.ToLocalTime().ToString("g"))
                : string.Empty;
            lastChecked.Visibility = lastChecked.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        checkButton.Click += (_, _) =>
        {
            if (s.Updates is { } service)
            {
                _ = service.CheckAsync(manual: true);
            }
        };
        if (s.Updates is { } svc)
        {
            svc.Changed += Refresh;
            stack.Unloaded += (_, _) => svc.Changed -= Refresh;
            stack.Loaded += (_, _) =>
            {
                svc.Changed -= Refresh; // never twice
                svc.Changed += Refresh;
                Refresh();
            };
        }

        lastChecked.FontSize = 11;
        lastChecked.TextWrapping = TextWrapping.Wrap;
        lastChecked.Margin = new Thickness(0, 4, 0, 0);
        lastChecked.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary);
        updates.Add(status);
        updates.Add(checkButton);
        updates.Add(lastChecked);
        Refresh();

        stack.AddSpaced(updates, 20);
        return stack;
    }

    private static UIElement Toggle(ShellServices s, string key, string title)
    {
        var toggle = new MacSwitch(s.App.Get<bool>(key), SwitchSize.Regular, on => s.App.Set(key, on));
        return new SettingsRow(null, title, null, toggle);
    }
}

/// <summary>The What's New page (spec 02e section 10) with Trayloom's own short changelog.</summary>
internal static class WhatsNewPage
{
    private sealed record Section(string Title, string[] Items);

    private static Section[] Notes => _loc_Notes.Value;

    private static readonly LocValue<Section[]> _loc_Notes = new(() => [
        new("Added",
        [
            Loc.T("The Features hub: install only what you use, with bundles, permissions and a restart-to-unload card."),
            Loc.T("A tabbed tray panel with Keep awake, Volume mixer, System, Network, Disks, Power, Utilities, Controls, Quick toggles and Wallpaper tabs."),
            Loc.T("A Settings window with a searchable sidebar, back and forward history and section anchors."),
            Loc.T("Central keyboard shortcuts page with conflict reporting."),
            Loc.T("Screen time: how long you used the PC today, and in which apps, in the Dynamic Island. Everything stays on your PC."),
            Loc.T("GitHub: unread notifications, your pull requests, reviews waiting for you and failing checks in the Dynamic Island. Off until you connect, through the GitHub CLI or an encrypted token."),
            Loc.T("Wi-Fi: see your network and its signal, turn Wi-Fi on or off and join nearby networks from the Dynamic Island and the tray."),
            Loc.T("Away mode: your windows are minimized when you leave the PC and come back in the same order when you return, optionally when the camera sees you."),
            Loc.T("Away mode can also close the apps you choose when you leave and open them again when you’re back. Chrome and Edge come back with the same windows and tabs; an app that asks to save something is left open, so nothing is lost."),
            Loc.T("Away mode can start when the camera stops seeing you, after a 10-second notice that any movement cancels. Off until you turn it on."),
            Loc.T("Modes: one-click workspaces for coding, studying and more. Save what’s open now as a mode and start it from the island, the tray, the Command Bar or a shortcut."),
            Loc.T("Bluetooth: see your devices and their battery in the Dynamic Island and the tray, turn Bluetooth on or off and get one alert when a device runs low."),
            Loc.T("GitHub while you code: the island follows the repository in front of you, with branch, changes, pull request and checks, and tells you when checks finish."),
            Loc.T("Claude Code’s pixel mascot opens a laptop and types on it in the Dynamic Island while Claude works, as it does in Claude Code, and waves when it needs you."),
            Loc.T("Google Calendar in the island: add your calendar’s secret address, see your events and join a Google Meet call in one click."),
            Loc.T("Create and edit Modes right in the Dynamic Island: name, colour and icon, “Use what’s open now”, apps, web pages and folders."),
            Loc.T("Timer alarm: a timer or a Pomodoro phase that ends now rings a sound you choose, with Stop and +1 min right in the closed island. The stopwatch never rings."),
            Loc.T("Modes can start by themselves when an app comes to the front or at a time you set, and the active mode shows as a small badge in the closed island."),
            Loc.T("Focus sessions: a Pomodoro that starts a mode and keeps the island quiet until your break."),
            Loc.T("Meeting heads-up: your next meeting appears in the closed island with a Join button, and you can check your microphone and camera first."),
            Loc.T("Dev servers in the island: see the local servers you run, open them, and stop one after a question. System processes are never touched."),
            Loc.T("Claude limit forecast: the Limits card says when a limit would run out at your pace, and the island can warn you before it does."),
        ]),
        new("Changed",
        [
            Loc.T("Every screen now follows the same look: cards, switches and the segmented tab bar."),
            Loc.T("Appearance can follow Windows or be forced to light or dark."),
            Loc.T("The island’s music Library switches between Apple Music, Spotify and YouTube Music, shows playlist covers, has Albums and Artists tabs, and stays on its page when the song changes."),
        ]),
        new("Fixed",
        [
            Loc.T("Away mode with the camera leaves and returns reliably: a restart no longer minimizes your windows, the camera light switches off when you are back, and a dark room never counts as you being away."),
            Loc.T("Trayloom uses much less memory and keeps the Dynamic Island smoother while an AI agent works."),
            Loc.T("The music Library opens taller, can be dragged to the height you like, and the source switch sits right above it."),
            Loc.T("The Notch shape works: choosing it attaches the island to the screen edge, Settings explains when it shows as a capsule, and the outline, companion side, previews and other displays follow the notch."),
            Loc.T("A playing song keeps its banner in the closed island while an AI agent works; the agent takes over only when it needs you."),
            Loc.T("The AI Agents page in the Dynamic Island scrolls smoothly: it no longer jumps to the top while an agent works, and its edges fade instead of being cut off."),
            Loc.T("Settings rows keep their titles and captions readable at any window width, tile labels show in full, switches line up, and tiles and tabs work with the keyboard."),
            Loc.T("Brightness keys step evenly, and the right monitor is used when several are connected."),
            Loc.T("Shelf and Command Bar windows keep their real size when they open."),
            Loc.T("Window layout on monitors with a different scale no longer overshoots the screen edge."),
        ]),
    ]);

    public static FrameworkElement Build()
    {
        var stack = new StackPanel { Margin = new Thickness(2, 2, 2, 0) };
        stack.Children.Add(UiKit.Text(Loc.T("What’s new in this version"), 18, FontWeights.Bold, ThemeKeys.TextPrimary));
        var version = UiKit.Text($"v{AppInfo.Version} · 2026-11-07", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        version.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(version);
        foreach (var section in Notes)
        {
            var heading = UiKit.Text(Loc.T(section.Title).ToUpperInvariant(), 11, FontWeights.Bold, ThemeKeys.TextSecondary);
            heading.Margin = new Thickness(0, 18, 0, 8);
            stack.Children.Add(heading);
            foreach (var item in section.Items)
            {
                var glyph = section.Title switch { "Added" => "", "Changed" => "", "Fixed" => "", _ => "" };
                var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = UiKit.Icon(glyph, 12, ThemeKeys.Accent, FontWeights.SemiBold);
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 2, 0, 0);
                row.Children.Add(icon);
                var text = UiKit.Text(item, 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                stack.Children.Add(row);
            }
        }

        return stack;
    }
}

/// <summary>The Support page (spec 02e section 11): no donation links, a GitHub star card.</summary>
internal static class SupportPage
{
    public static FrameworkElement Build()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 26), MaxWidth = 510 };
        var badge = new Border
        {
            Width = 78,
            Height = 78,
            CornerRadius = new CornerRadius(39),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(Color.FromRgb(0x2C, 0x2C, 0x30), Colors.Black, new Point(0, 0), new Point(1, 1)),
            Child = UiKit.Icon(Glyphs.Heart, 29, ThemeKeys.OnAccent, FontWeights.SemiBold),
        };
        stack.Children.Add(badge);
        var heading = UiKit.Text(Loc.T("Help Trayloom keep growing"), 18, FontWeights.Bold, ThemeKeys.TextPrimary);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(heading);
        var message = UiKit.Text(Loc.T("Trayloom stays free and local, with no ads or usage tracking. If it helps you, a star on GitHub is the best way to help it grow."), 13.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        message.TextAlignment = TextAlignment.Center;
        message.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(message);

        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 18, 0, 0), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var star = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xD6, 0x0A)),
            Child = UiKit.Icon(Glyphs.Star, 18, ThemeKeys.Yellow, FontWeights.SemiBold),
        };
        row.Children.Add(star);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(UiKit.Text(Loc.T("Financial support is never expected. A star on GitHub helps more people discover Trayloom and makes a real difference to its development."), 13.5, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true));
        var button = UiKit.Button(Loc.T("Star Trayloom on GitHub"), ButtonKind.Bordered, 13, Glyphs.Star, () => UiKit.OpenUrl(AppInfo.RepositoryUrl));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(14, 7, 14, 7);
        button.Margin = new Thickness(0, 12, 0, 0);
        text.Children.Add(button);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        card.Child = row;
        stack.Children.Add(card);
        var thanks = UiKit.Text(ThemeManager.IsLight ? Loc.T("Thank you for being here. \U0001F5A4") : Loc.T("Thank you for being here. \U0001F90D"), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        thanks.HorizontalAlignment = HorizontalAlignment.Center;
        thanks.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(thanks);
        return stack;
    }
}
