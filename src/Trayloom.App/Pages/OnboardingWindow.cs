// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>
/// First-run introduction (spec 02-settings section 7), 540 x 600: welcome, purpose (bundles / features), start with Windows,
/// permissions, done (where the tray icon is and how to pin it next to the clock).
/// </summary>
internal sealed class OnboardingWindow : Window
{
    private readonly ShellServices _s;
    private readonly ScrollViewer _body = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _back;
    private readonly Button _next;
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private string? _presetId;
    private IReadOnlyList<string> _presetKeys = [];
    private const int LastStep = 4;
    private int _step;
    private bool _login;

    public OnboardingWindow(ShellServices services)
    {
        _s = services;
        Title = Loc.T("Welcome to Trayloom");
        Width = 540;
        Height = 600;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiKit.TextFont;
        this.Themed(StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(this);

        // Start from what is installed now (a clean install starts with Essentials).
        foreach (var e in FeatureCatalog.All.Where(e => _s.Runtime.IsAvailable(e.Id)))
        {
            _selection.Add(e.Id);
        }

        // Essentials is preselected on a fresh run (its features are what a clean install starts with).
        _presetId = FeatureCatalog.Presets.FirstOrDefault(p => p.Features.Count == _selection.Count && p.Features.All(_selection.Contains))?.Id;
        _presetKeys = FeatureCatalog.Presets.FirstOrDefault(p => p.Id == _presetId)?.EnableKeys ?? [];
        _step = Math.Clamp(_s.App.Get<int>(AppKeys.OnboardingStep), 0, LastStep);
        // Launch at login defaults to on for an installed copy (never overrides a choice the user already made).
        _login = SafeLoginEnabled() || (InstallManager.IsInstalledCopy && !_s.App.IsSet(AppKeys.LaunchAtLoginWanted));
        _back = UiKit.Button(Loc.T("Back"), ButtonKind.Bordered, 13, onClick: () => Go(_step - 1));
        _next = UiKit.Button(Loc.T("Continue"), ButtonKind.Prominent, 14, onClick: Advance);
        _next.Padding = new Thickness(18, 7, 18, 7);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.SetResourceReference(Panel.BackgroundProperty, ThemeKeys.SettingsWindow);
        root.Children.Add(_body);
        var footer = new Grid { Margin = new Thickness(0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var bar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 14, 20, 14), Child = footer };
        bar.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(_back);
        Grid.SetColumn(_dots, 1);
        footer.Children.Add(_dots);
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_next, 2);
        footer.Children.Add(_next);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        Content = root;
        Go(_step);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, LastStep);
        _s.App.Set(AppKeys.OnboardingStep, _step);
        _back.IsEnabled = _step > 0;
        _next.Content = _step == LastStep ? Loc.T("Open Trayloom") : Loc.T("Continue");
        _dots.Children.Clear();
        for (var i = 0; i <= LastStep; i++)
        {
            var dot = new Border { Height = 7, Width = i == _step ? 18 : 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _step ? ThemeKeys.Accent : ThemeKeys.Track);
            _dots.Children.Add(dot);
        }

        _body.Content = _step switch
        {
            0 => Welcome(),
            1 => Purpose(),
            2 => Startup(),
            3 => Permissions(),
            _ => Done(),
        };
        _body.ScrollToTop();
    }

    private void Advance()
    {
        if (_step == 1)
        {
            // Pressing Continue applies the selection.
            _s.Runtime.ReplaceAvailable(_selection, _presetKeys);
        }

        if (_step == 2)
        {
            ApplyLogin();
        }

        if (_step == LastStep)
        {
            _s.App.Set(AppKeys.HasOnboarded, true);
            _s.App.Set(AppKeys.OnboardingStep, 0);
            Close();
            // Show where the flyout lives: pop it up next to the clock.
            _s.Shell.ShowPanelAtTray();
            return;
        }

        Go(_step + 1);
    }

    // ------------------------------------------------------------------------------------------------ steps

    private static Border Hero(double height, double logo, string? title, string? subtitle) => UiParts.Hero(height, logo, title, subtitle);

    private UIElement Welcome()
    {
        var stack = new StackPanel();
        stack.Children.Add(Hero(220, 130, "Trayloom", Loc.T("A discreet tray utility that makes everyday Windows more practical.")));
        var content = new StackPanel { Margin = new Thickness(24) };
        var what = UiKit.Text(Loc.T("Trayloom is a set of small tools for your PC that lives in the tray, next to the clock. Everything is optional: on the next page you choose what to install."), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        what.Margin = new Thickness(0, 0, 0, 18);
        content.Children.Add(what);
        content.Children.Add(FeatureLine("\uE945", Loc.T("Energy under control"), Loc.T("Keep the PC awake for as long as you want, even with the lid closed.")));
        content.Children.Add(FeatureLine("\uE9D9", Loc.T("A clear view of the system"), Loc.T("CPU, GPU and battery usage, memory pressure and network speed in real time.")));
        content.Children.Add(FeatureLine("\uE7C4", Loc.T("Mouse and windows, your way"), Loc.T("Reversed mouse scrolling and a window switcher with thumbnails.")));
        stack.Children.Add(content);
        return stack;
    }

    private static UIElement FeatureLine(string glyph, string title, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(Color.FromRgb(0x3C, 0x3C, 0x42), Colors.Black, new Point(0, 0), new Point(1, 1)),
            Child = UiKit.Icon(glyph, 13, ThemeKeys.OnAccent, FontWeights.SemiBold),
        };
        row.Children.Add(tile);
        var col = new StackPanel();
        col.Children.Add(UiKit.Text(title, 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        col.Children.Add(UiKit.Text(text, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        Grid.SetColumn(col, 2);
        row.Children.Add(col);
        return row;
    }

    private UIElement Purpose()
    {
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        var head = UiKit.Icon("\uE8A9", 26, ThemeKeys.Accent);
        stack.Children.Add(head);
        var title = UiKit.Text(Loc.T("What brought you here?"), 21, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var sub = UiKit.Text(Loc.T("Choose a ready setup or select exactly what you want to use."), 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 4, 0, 14);
        stack.Children.Add(sub);

        foreach (var preset in FeatureCatalog.Presets)
        {
            var selected = _presetId == preset.Id;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiKit.Icon(preset.Glyph, 15, ThemeKeys.Accent, FontWeights.SemiBold));
            var col = new StackPanel();
            col.Children.Add(UiKit.Text(preset.Name, 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
            col.Children.Add(UiKit.Text(preset.Description, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
            Grid.SetColumn(col, 1);
            row.Children.Add(col);
            if (selected)
            {
                var check = UiKit.Icon("\uE930", 16, ThemeKeys.Accent);
                Grid.SetColumn(check, 2);
                row.Children.Add(check);
            }

            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Child = row, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, selected ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
            card.SetResourceReference(Border.BorderBrushProperty, selected ? ThemeKeys.Accent : ThemeKeys.Divider);
            var captured = preset;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _presetId = captured.Id;
                _presetKeys = captured.EnableKeys;
                _selection.Clear();
                foreach (var id in captured.Features)
                {
                    _selection.Add(id);
                }

                Go(1);
            };
            stack.Children.Add(card);
        }

        var heading = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(UiKit.Text(Loc.T("Features").ToUpper(Loc.Culture), 11, FontWeights.Bold, ThemeKeys.TextSecondary));
        var total = FeatureCatalog.All.Count(e => e.CanInstall);
        var count = UiKit.Text(Loc.F("{0} of {1} features installed", _selection.Count, total), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        Grid.SetColumn(count, 1);
        heading.Children.Add(count);
        stack.Children.Add(heading);

        foreach (var group in new[] { FeatureGroup.WindowsTaskbar, FeatureGroup.MouseKeyboard, FeatureGroup.ClipboardFiles, FeatureGroup.Sound, FeatureGroup.EnergyDisplay, FeatureGroup.Tools, FeatureGroup.Island, FeatureGroup.Monitor })
        {
            // Features that do not exist on Windows are not offered here at all (the Features hub lists them greyed out).
            var offered = FeatureCatalog.InGroup(group).Where(e => e.CanInstall).ToList();
            if (offered.Count == 0)
            {
                continue;
            }

            var gh = UiKit.Text(group.DisplayName(), 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            gh.Margin = new Thickness(2, 10, 0, 6);
            stack.Children.Add(gh);
            foreach (var entry in offered)
            {
                stack.Children.Add(FeatureCard(entry));
            }
        }

        var note = UiKit.Text(Loc.T("You can add or remove features later in Settings."), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        note.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(note);
        return stack;
    }

    private UIElement FeatureCard(FeatureCatalogEntry entry)
    {
        var selected = _selection.Contains(entry.Id);
        var blocked = !entry.CanInstall;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        if (selected)
        {
            tile.Background = new LinearGradientBrush(Color.FromRgb(0x3C, 0x3C, 0x42), Colors.Black, new Point(0, 0), new Point(1, 1));
            tile.Child = UiKit.Icon(entry.Glyph, 14, ThemeKeys.OnAccent);
        }
        else
        {
            tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            tile.Child = UiKit.Icon(entry.Glyph, 14, ThemeKeys.TextSecondary);
        }

        row.Children.Add(tile);
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(UiKit.Text(entry.Title, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        col.Children.Add(UiKit.Text(entry.Description, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true, trimming: TextTrimming.CharacterEllipsis).Also(t => t.MaxHeight = 32));
        Grid.SetColumn(col, 2);
        row.Children.Add(col);
        var check = UiKit.Icon(selected ? "\uE930" : "\uEA3A", 16, selected ? ThemeKeys.Accent : ThemeKeys.TextTertiary);
        Grid.SetColumn(check, 3);
        row.Children.Add(check);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = row, Cursor = blocked ? Cursors.Arrow : Cursors.Hand };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        if (blocked)
        {
            card.Opacity = 0.4;
            card.ToolTip = entry.UnavailableReason;
        }
        else
        {
            card.MouseLeftButtonUp += (_, _) =>
            {
                _presetId = null;
                _presetKeys = [];
                var on = !_selection.Contains(entry.Id);
                void Set(string id, bool value)
                {
                    if (value)
                    {
                        _selection.Add(id);
                    }
                    else
                    {
                        _selection.Remove(id);
                    }
                }

                Set(entry.Id, on);
                if (entry.Id == "notch")
                {
                    foreach (var id in on ? FeatureCatalog.IslandInitialExtensions : FeatureCatalog.IslandExtensions.Select(x => x.Id).ToList())
                    {
                        Set(id, on);
                    }
                }

                var offset = _body.VerticalOffset;
                Go(1);
                _body.ScrollToVerticalOffset(offset);
            };
        }

        return card;
    }

    private UIElement Permissions()
    {
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        var head = UiKit.Icon("\uE8D7", 26, ThemeKeys.Accent);
        stack.Children.Add(head);
        var title = UiKit.Text(Loc.T("Permissions"), 21, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var sub = UiKit.Text(Loc.T("What each permission does and which features use it. Other access is requested only when you use it."), 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 4, 0, 14);
        stack.Children.Add(sub);

        var selectedPerms = FeatureCatalog.All.Where(e => _selection.Contains(e.Id))
            .SelectMany(e => e.Permissions).Where(p => p is AppPermission.Accessibility or AppPermission.ScreenRecording).Distinct().ToList();
        var card = new SettingsCard(Loc.T("Permissions for your choices"));
        if (selectedPerms.Count == 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(UiKit.Icon("\uE930", 14, ThemeKeys.Green));
            var t = UiKit.Text(Loc.T("You do not need to grant any permission to finish setup."), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
            t.Margin = new Thickness(8, 0, 0, 0);
            line.Children.Add(t);
            card.Add(line);
        }
        else
        {
            foreach (var p in selectedPerms)
            {
                var status = PermissionService.Check(p);
                var row = new StackPanel();
                var h = new StackPanel { Orientation = Orientation.Horizontal };
                h.Children.Add(UiKit.Icon(status.State == PermissionState.Granted ? "\uE930" : "\uE7BA", 14, status.State == PermissionState.Granted ? ThemeKeys.Green : ThemeKeys.Orange));
                var n = UiKit.Text(PermissionService.Name(p), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
                n.Margin = new Thickness(8, 0, 0, 0);
                h.Children.Add(n);
                var st = UiKit.Text(status.State == PermissionState.Granted ? Loc.T("Granted") : Loc.T("Not granted"), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
                st.Margin = new Thickness(8, 0, 0, 0);
                h.Children.Add(st);
                row.Children.Add(h);
                row.Children.Add(UiKit.Text(PermissionService.Explainer(p), 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true).Also(x => x.Margin = new Thickness(22, 3, 0, 0)));
                var users = FeatureCatalog.UsingPermission(p).Where(e => _selection.Contains(e.Id)).Select(e => e.Title).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
                if (users.Count > 0)
                {
                    row.Children.Add(UiKit.Text(Loc.F("Used by {0}", string.Join(", ", users)), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true).Also(x => x.Margin = new Thickness(22, 3, 0, 0)));
                }

                card.Add(row);
            }
        }

        stack.Children.Add(card);
        // Disclosure (collapsed): the remaining permissions, without the ones already listed above.
        var other = new SettingsCard();
        var disclosure = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        disclosure.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        disclosure.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        disclosure.Children.Add(UiKit.Text(Loc.T("Other permissions"), 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 10, ThemeKeys.TextSecondary, FontWeights.Bold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        Grid.SetColumn(chevron, 1);
        disclosure.Children.Add(chevron);
        other.Add(disclosure);
        other.AddCaption(Loc.T("Optional. Grant these now or later, when a feature needs them."));
        var rest = PermissionsPage.Build(_s, selectedPerms);
        rest.Visibility = Visibility.Collapsed;
        other.Add(rest);
        disclosure.MouseLeftButtonUp += (_, _) =>
        {
            var open = rest.Visibility != Visibility.Visible;
            rest.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.RenderTransform = new RotateTransform(open ? 90 : 0);
        };
        stack.AddSpaced(other, 14);
        var foot = UiKit.Text(Loc.T("Some features need Trayloom to run as administrator."), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        foot.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(foot);
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ start with Windows

    private static bool SafeLoginEnabled()
    {
        try
        {
            return AutoStart.IsEnabled;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ApplyLogin()
    {
        try
        {
            if (_login != SafeLoginEnabled())
            {
                AutoStart.SetEnabled(_login);
            }

            _s.App.Set(AppKeys.LaunchAtLoginWanted, _login);
        }
        catch (Exception ex)
        {
            _s.Log.Error("Changing launch at login failed", ex);
        }
    }

    private UIElement Startup()
    {
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        var head = UiKit.Icon("", 26, ThemeKeys.Accent);
        stack.Children.Add(head);
        var title = UiKit.Text(Loc.T("Start with Windows"), 21, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var sub = UiKit.Text(Loc.T("Trayloom lives in the tray, so it is most useful when it is already there after you sign in."), 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 4, 0, 16);
        stack.Children.Add(sub);

        var card = new SettingsCard();
        card.Add(new SettingsRow("", Loc.T("Launch at login"), Loc.T("Opens by itself every time you turn on your PC."), new MacSwitch(_login, SwitchSize.Regular, on => _login = on)));
        if (!InstallManager.IsInstalledCopy && !InstallManager.IsDevelopmentBuild)
        {
            card.AddDivider();
            card.Add(new SettingsRow("", Loc.T("Trayloom is not installed yet"), Loc.F("It is running from {0}. Install it so the Start menu and the login entry keep working if you move or delete that folder.", InstallManager.CurrentDirectory),
                UiKit.Button(Loc.T("Install…"), ButtonKind.Bordered, 12, onClick: () => _s.Shell.ShowInstallOffer())));
        }

        stack.Children.Add(card);

        var ways = new SettingsCard(Loc.T("Ways to open Trayloom"));
        ways.Add(new SettingsRow("", Loc.T("The tray icon"), Loc.T("Click the Trayloom icon next to the clock for the panel; right-click it for the menu.")));
        ways.AddDivider();
        ways.Add(new SettingsRow("", Loc.T("The Start menu"), Loc.T("Press the Windows key, type “Trayloom” and press Enter. This opens Settings if it is already running.")));
        stack.AddSpaced(ways, 16);
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ done

    private UIElement Done()
    {
        var stack = new StackPanel();
        stack.Children.Add(Hero(150, 76, Loc.T("All set!"), Loc.T("Trayloom is running and looking after your PC.")));
        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 16) };
        var look = UiKit.Text(Loc.T("Your Trayloom icon is next to the clock"), 15, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        look.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(look);
        var hint = UiKit.Text(Loc.T("Click it to open the panel. If you do not see it, click the ^ arrow to show hidden icons."), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, 4, 0, 12);
        body.Children.Add(hint);
        var illustration = TrayIllustration.Build();
        illustration.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(illustration);

        var pin = new SettingsCard();
        pin.Add(new SettingsRow("", Loc.T("Pin the icon to the taskbar corner"),
            Loc.T("Windows does not let apps pin themselves. Open Taskbar settings, expand “Other system tray icons” and switch Trayloom on."),
            UiKit.Button(Loc.T("Open Taskbar settings"), ButtonKind.Bordered, 12, onClick: () => UiKit.OpenUrl("ms-settings:taskbar"))));
        body.AddSpaced(pin, 14);
        stack.Children.Add(body);
        return stack;
    }
}

/// <summary>A small drawing of the taskbar corner with the Trayloom icon highlighted and an arrow pointing at it.</summary>
internal static class TrayIllustration
{
    public static FrameworkElement Build()
    {
        const double W = 440;
        const double H = 124;
        var canvas = new Canvas { Width = W, Height = H };

        // Taskbar strip (fixed dark colours: it depicts Windows, not our theme).
        var strip = new Border { Width = W, Height = 46, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1) };
        Canvas.SetTop(strip, H - 46);
        canvas.Children.Add(strip);

        // App icons on the left of the taskbar.
        var xs = new[] { 150.0, 182.0, 214.0, 246.0 };
        for (var i = 0; i < xs.Length; i++)
        {
            var app = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(i == 0 ? (byte)0xB0 : (byte)0x50, 255, 255, 255)) };
            Canvas.SetLeft(app, xs[i]);
            Canvas.SetTop(app, H - 46 + 12);
            canvas.Children.Add(app);
        }

        // Tray: chevron, Trayloom (highlighted), network, volume, clock.
        var chevron = Glyph("", 11, Colors.White, 0.85);
        Canvas.SetLeft(chevron, 286);
        Canvas.SetTop(chevron, H - 46 + 15);
        canvas.Children.Add(chevron);

        var iconX = 316.0;
        var ring = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4D, 0xA3, 0xFF)) };
        Canvas.SetLeft(ring, iconX - 4);
        Canvas.SetTop(ring, H - 46 + 8);
        canvas.Children.Add(ring);
        ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var mark = new System.Windows.Shapes.Path { Data = BrandMark.Geometry, Stretch = Stretch.Uniform, Width = 22, Height = 22 * BrandMark.Height / BrandMark.Width, Fill = Brushes.White };
        Canvas.SetLeft(mark, iconX + 1);
        Canvas.SetTop(mark, H - 46 + 16);
        canvas.Children.Add(mark);

        var wifi = Glyph("", 12, Colors.White, 0.85);
        Canvas.SetLeft(wifi, 356);
        Canvas.SetTop(wifi, H - 46 + 15);
        canvas.Children.Add(wifi);
        var volume = Glyph("", 12, Colors.White, 0.85);
        Canvas.SetLeft(volume, 378);
        Canvas.SetTop(volume, H - 46 + 15);
        canvas.Children.Add(volume);
        var clock = new TextBlock { Text = "9:41", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)), FontFamily = UiKit.TextFont };
        Canvas.SetLeft(clock, 403);
        Canvas.SetTop(clock, H - 46 + 15);
        canvas.Children.Add(clock);

        // Callout pill + arrow pointing at the icon.
        var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5), Background = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)) };
        pill.Child = new TextBlock { Text = "Trayloom", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = UiKit.TextFont };
        Canvas.SetLeft(pill, iconX + 13 - 36);
        Canvas.SetTop(pill, 6);
        canvas.Children.Add(pill);
        var arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 0,24 M -6,17 L 0,25 L 6,17"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)),
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(arrow, iconX + 13);
        Canvas.SetTop(arrow, 36);
        canvas.Children.Add(arrow);

        return new Border { Child = canvas, Padding = new Thickness(0), Width = W, Height = H };
    }

    private static TextBlock Glyph(string glyph, double size, Color color, double opacity) =>
        new() { Text = glyph, FontFamily = UiKit.IconFont, FontSize = size, Foreground = new SolidColorBrush(color), Opacity = opacity };
}

internal static class UiExtensions
{
    /// <summary>Runs <paramref name="action"/> on the value and returns it (inline initialisation).</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
