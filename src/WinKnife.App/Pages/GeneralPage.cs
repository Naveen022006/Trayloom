// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>The app-wide cards of the General page: Launch at login, Language, Appearance, Feedback (spec 02a section 2).</summary>
internal static class GeneralPage
{
    private static readonly (string Code, string Name)[] Languages =
    [
        ("en-US", "English (US)"), ("pt-BR", "Português (Brasil)"), ("tr", "Türkçe"), ("ru", "Русский"),
        ("es", "Español"), ("sk", "Slovenčina"), ("de", "Deutsch"), ("fr", "Français"), ("it", "Italiano"),
        ("ja", "日本語"), ("ko", "한국어"), ("uk", "Українська"),
        ("zh-Hans", "简体中文"), ("zh-TW", "繁體中文（台灣）"), ("zh-HK", "繁體中文（香港）"),
    ];

    public static FrameworkElement Build(ShellServices s, Action rebuild)
    {
        var stack = new StackPanel();
        if (!s.App.Get<bool>(AppKeys.GettingStartedDismissed))
        {
            stack.AddSpaced(GettingStartedCard(s, rebuild), 20);
        }

        stack.AddSpaced(LaunchCard(s, rebuild), 20);
        stack.AddSpaced(AppearanceCard(s, rebuild), 20);
        // The Feedback card is contributed by the Tools project (it owns the feedback window).
        return stack;
    }

    /// <summary>"Getting started": where the tray icon is, how to pin it, how to add features, install, report a problem.</summary>
    private static UIElement GettingStartedCard(ShellServices s, Action rebuild)
    {
        var card = new SettingsCard();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(UiKit.Text("Getting started", 14, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var hide = UiKit.Button("Hide", ButtonKind.Plain, 12, onClick: () =>
        {
            s.App.Set(AppKeys.GettingStartedDismissed, true);
            rebuild();
        });
        hide.ToolTip = "Hide this card. “Review introduction” in About brings the tour back.";
        Grid.SetColumn(hide, 1);
        head.Children.Add(hide);
        card.Add(head);

        card.Add(new SettingsRow(Glyphs.Search, "Find the WinKnife icon", "It sits next to the clock; click it for the panel, right-click it for the menu. If it is hidden, click the ^ arrow first.",
            UiKit.Button("Pin it next to the clock", ButtonKind.Bordered, 12, onClick: () => UiKit.OpenUrl("ms-settings:taskbar"))));
        card.AddDivider();
        var installed = s.Runtime.AvailableCount;
        var total = s.Runtime.InstallableCount;
        card.Add(new SettingsRow(Glyphs.Add, "Add features", $"{installed} of {total} features installed. Install only what you use; the panel and Settings grow with them.",
            UiKit.Button("Open Features", ButtonKind.Prominent, 12, onClick: () => s.Shell.OpenFeatures())));
        if (!InstallManager.IsInstalledCopy && !InstallManager.IsDevelopmentBuild)
        {
            card.AddDivider();
            card.Add(new SettingsRow("", "Install WinKnife", "Add it to the Start menu and keep it if you delete the download. No administrator password needed.",
                UiKit.Button("Install…", ButtonKind.Bordered, 12, onClick: () => s.Shell.ShowInstallOffer())));
        }

        card.AddDivider();
        card.Add(new SettingsRow(Glyphs.Info, "Take the tour again", "The short introduction: bundles, start with Windows, where the icon is.",
            UiKit.Button("Review introduction", ButtonKind.Bordered, 12, onClick: () => s.Shell.ShowOnboarding())));
        card.AddDivider();
        card.Add(new SettingsRow(Glyphs.Warning, "Something not working?", "Opens the folder with WinKnife’s log file, to read or send along.",
            UiKit.Button("Report a problem", ButtonKind.Bordered, 12, onClick: () => s.Shell.ReportProblem())));
        return card;
    }

    private static UIElement LaunchCard(ShellServices s, Action rebuild)
    {
        var card = new SettingsCard();
        string? error = null;
        var enabled = SafeEnabled();
        var toggle = new MacSwitch(enabled, SwitchSize.Regular);
        toggle.Toggled += on =>
        {
            s.App.Set(AppKeys.LaunchAtLoginWanted, on);
            try
            {
                AutoStart.SetEnabled(on);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                s.Log.Error("Changing launch at login failed", ex);
                s.App.Set(AppKeys.LaunchAtLoginWanted, SafeEnabled());
            }

            rebuild();
        };
        card.Add(new SettingsRow("", "Launch at login", "Opens by itself every time you turn on your PC.", toggle));
        if (enabled && AutoStart.IsDisabledByUser)
        {
            var note = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
            note.Children.Add(UiKit.Text("The login item is registered, but Windows still needs your approval. In Settings > Apps > Startup, allow WinKnife to start at sign-in.", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
            var open = UiKit.Button("Open Windows Settings…", ButtonKind.Bordered, 12, onClick: () => UiKit.OpenUrl("ms-settings:startupapps"));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 6, 0, 0);
            note.Children.Add(open);
            card.Add(note);
        }
        else if (error is not null)
        {
            var err = UiKit.Text(error, 11.5, FontWeights.Normal, ThemeKeys.Red, wrap: true);
            err.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
            card.Add(err);
        }

        card.AddDivider();
        var current = s.App.Get<string>(AppKeys.AppLanguage);
        var combo = new ComboBox { MinWidth = 170, ItemsSource = Languages.Select(l => l.Name).ToList() };
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Code == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                s.App.Set(AppKeys.AppLanguage, Languages[combo.SelectedIndex].Code);
            }
        };
        card.Add(new SettingsRow("", "Language", null, combo));
        return card;
    }

    private static bool SafeEnabled()
    {
        try
        {
            return AutoStart.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static UIElement AppearanceCard(ShellServices s, Action rebuild)
    {
        var card = new SettingsCard("Appearance");
        var current = s.App.Get<string>(AppKeys.AppAppearance);
        var grid = new Grid();
        var modes = new[] { ("system", "System"), ("light", "Light"), ("dark", "Dark") };
        for (var i = 0; i < modes.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (id, label) = modes[i];
            var selected = id == current;
            var cell = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Cursor = Cursors.Hand, Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0), Background = Brushes.Transparent };
            if (selected)
            {
                cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            }

            var col = new StackPanel();
            var thumb = Thumbnail(id);
            var frame = new Border
            {
                Height = 66,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Child = thumb,
                ClipToBounds = true,
            };
            frame.SetResourceReference(Border.BorderBrushProperty, selected ? ThemeKeys.Accent : ThemeKeys.Divider);
            col.Children.Add(frame);
            var text = UiKit.Text(label, 12, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(text);
            cell.Child = col;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                s.App.Set(AppKeys.AppAppearance, id);
                ThemeManager.SetAppearance(id switch { "light" => AppAppearance.Light, "dark" => AppAppearance.Dark, _ => AppAppearance.System });
                rebuild();
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        card.Add(grid);
        card.AddCaption("Applies to WinKnife’s own windows and panels, not to the whole PC.");
        return card;
    }

    /// <summary>"Three little desktops with a window": light, dark, or a diagonal split for System.</summary>
    private static UIElement Thumbnail(string id)
    {
        FrameworkElement Desktop(bool dark)
        {
            var g = new Grid();
            g.Background = new LinearGradientBrush(
                dark ? Color.FromRgb(0x33, 0x38, 0x57) : Color.FromRgb(0xCC, 0xE0, 0xFF),
                dark ? Color.FromRgb(0x12, 0x12, 0x21) : Color.FromRgb(0x94, 0xB8, 0xF2),
                new Point(0, 0), new Point(1, 1));
            var window = new Border
            {
                Margin = new Thickness(14, 12, 14, 0),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Background = new SolidColorBrush(dark ? Color.FromRgb(0x30, 0x30, 0x30) : Color.FromRgb(0xFC, 0xFC, 0xFC)),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var title = new Border { CornerRadius = new CornerRadius(5, 5, 0, 0), Background = new SolidColorBrush(dark ? Color.FromRgb(0x38, 0x38, 0x38) : Color.FromRgb(0xED, 0xED, 0xED)) };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in new[] { "#FF5F57", "#FFBD2E", "#29C740" })
            {
                dots.Children.Add(new Ellipse { Width = 5, Height = 5, Margin = new Thickness(0, 0, 3, 0), Fill = (Brush)new BrushConverter().ConvertFromString(c)! });
            }

            title.Child = dots;
            inner.Children.Add(title);
            var body = new Grid { Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFC, 0xFC, 0xFC)) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new Border { Background = new SolidColorBrush(dark ? Color.FromRgb(0x29, 0x29, 0x29) : Color.FromRgb(0xE6, 0xE6, 0xE6)) };
            var sideBars = new StackPanel { Margin = new Thickness(6) };
            foreach (var w in new[] { 16, 20, 12 })
            {
                sideBars.Children.Add(new Border { Height = 3, Width = w, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(dark ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0xC2, 0xC2, 0xC2)) });
            }

            side.Child = sideBars;
            body.Children.Add(side);
            var content = new StackPanel { Margin = new Thickness(8) };
            foreach (var w in new[] { 40, 30, 36 })
            {
                content.Children.Add(new Border { Height = 3, Width = w, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 5), Background = new SolidColorBrush(dark ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0xC2, 0xC2, 0xC2)) });
            }

            Grid.SetColumn(content, 1);
            body.Children.Add(content);
            Grid.SetRow(body, 1);
            inner.Children.Add(body);
            window.Child = inner;
            g.Children.Add(window);
            return g;
        }

        if (id == "light")
        {
            return Desktop(false);
        }

        if (id == "dark")
        {
            return Desktop(true);
        }

        var both = new Grid();
        both.Children.Add(Desktop(false));
        var dark = Desktop(true);
        dark.Clip = Geometry.Parse("M 200,0 L 200,70 L 0,70 Z");
        both.Children.Add(dark);
        both.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width;
            var h = e.NewSize.Height;
            dark.Clip = new PathGeometry([new PathFigure(new Point(w, 0), [new LineSegment(new Point(w, h), true), new LineSegment(new Point(0, h), true)], true)]);
        };
        return both;
    }

}
