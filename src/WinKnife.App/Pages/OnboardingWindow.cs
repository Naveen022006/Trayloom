// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>First-run introduction (spec 02-settings section 7): welcome, purpose, permissions, done. 540 x 600.</summary>
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
    private int _step;

    public OnboardingWindow(ShellServices services)
    {
        _s = services;
        Title = "Welcome to WinKnife";
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

        _presetId = FeatureCatalog.Presets[0].Id;
        _step = Math.Clamp(_s.App.Get<int>(AppKeys.OnboardingStep), 0, 3);
        _back = UiKit.Button("Back", ButtonKind.Bordered, 13, onClick: () => Go(_step - 1));
        _next = UiKit.Button("Continue", ButtonKind.Prominent, 14, onClick: Advance);
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
        _step = Math.Clamp(step, 0, 3);
        _s.App.Set(AppKeys.OnboardingStep, _step);
        _back.IsEnabled = _step > 0;
        _next.Content = _step == 3 ? "Open WinKnife" : "Continue";
        _dots.Children.Clear();
        for (var i = 0; i < 4; i++)
        {
            var dot = new Border { Height = 7, Width = i == _step ? 18 : 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _step ? ThemeKeys.Accent : ThemeKeys.Track);
            _dots.Children.Add(dot);
        }

        _body.Content = _step switch
        {
            0 => Welcome(),
            1 => Purpose(),
            2 => Permissions(),
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

        if (_step == 3)
        {
            _s.App.Set(AppKeys.HasOnboarded, true);
            _s.App.Set(AppKeys.OnboardingStep, 0);
            Close();
            return;
        }

        Go(_step + 1);
    }

    // ------------------------------------------------------------------------------------------------ steps

    private static Border Hero(double height, double logo, string? title, string? subtitle)
    {
        var hero = new Border
        {
            Height = height,
            Background = new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0x30, 0x30, 0x36), 0),
                new GradientStop(Color.FromRgb(0x12, 0x12, 0x15), 0.45),
                new GradientStop(Colors.Black, 1),
            ], new Point(0, 0), new Point(1, 1)),
        };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(BrandMark.Create(logo, ThemeKeys.OnAccent));
        if (title is not null)
        {
            var t = new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            col.Children.Add(t);
        }

        if (subtitle is not null)
        {
            var sub = new TextBlock { Text = subtitle, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 255, 255, 255)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Margin = new Thickness(0, 6, 0, 0) };
            col.Children.Add(sub);
        }

        hero.Child = col;
        return hero;
    }

    private UIElement Welcome()
    {
        var stack = new StackPanel();
        stack.Children.Add(Hero(220, 130, "WinKnife", "A discreet tray utility that makes everyday Windows more practical."));
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(FeatureLine("", "Energy under control", "Keep the PC awake for as long as you want, even with the lid closed."));
        content.Children.Add(FeatureLine("", "A clear view of the system", "CPU, GPU and battery usage, memory pressure and network speed in real time."));
        content.Children.Add(FeatureLine("", "Mouse and windows, your way", "Reversed mouse scrolling and a window switcher with thumbnails."));
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
        var head = UiKit.Icon("", 26, ThemeKeys.Accent);
        stack.Children.Add(head);
        var title = UiKit.Text("What brought you here?", 21, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var sub = UiKit.Text("Choose a ready setup or select exactly what you want to use.", 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
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
                var check = UiKit.Icon("", 16, ThemeKeys.Accent);
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
        heading.Children.Add(UiKit.Text("FEATURES", 11, FontWeights.Bold, ThemeKeys.TextSecondary));
        var total = FeatureCatalog.All.Count(e => e.CanInstall);
        var count = UiKit.Text($"{_selection.Count} of {total} features installed", 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        Grid.SetColumn(count, 1);
        heading.Children.Add(count);
        stack.Children.Add(heading);

        foreach (var group in new[] { FeatureGroup.WindowsTaskbar, FeatureGroup.MouseKeyboard, FeatureGroup.ClipboardFiles, FeatureGroup.Sound, FeatureGroup.EnergyDisplay, FeatureGroup.Tools, FeatureGroup.Island, FeatureGroup.Monitor })
        {
            var gh = UiKit.Text(group.DisplayName(), 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            gh.Margin = new Thickness(2, 10, 0, 6);
            stack.Children.Add(gh);
            foreach (var entry in FeatureCatalog.InGroup(group))
            {
                stack.Children.Add(FeatureCard(entry));
            }
        }

        var note = UiKit.Text("You can add or remove features later in Settings.", 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
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
        var check = UiKit.Icon(selected ? "" : "", 16, selected ? ThemeKeys.Accent : ThemeKeys.TextTertiary);
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
        var head = UiKit.Icon("", 26, ThemeKeys.Accent);
        stack.Children.Add(head);
        var title = UiKit.Text("Permissions", 21, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var sub = UiKit.Text("What each permission does and which features use it. Other access is requested only when you use it.", 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 4, 0, 14);
        stack.Children.Add(sub);

        var selectedPerms = FeatureCatalog.All.Where(e => _selection.Contains(e.Id))
            .SelectMany(e => e.Permissions).Where(p => p is AppPermission.Accessibility or AppPermission.ScreenRecording).Distinct().ToList();
        var card = new SettingsCard("Permissions for your choices");
        if (selectedPerms.Count == 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(UiKit.Icon("", 14, ThemeKeys.Green));
            var t = UiKit.Text("You do not need to grant any permission to finish setup.", 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
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
                h.Children.Add(UiKit.Icon(status.State == PermissionState.Granted ? "" : "", 14, status.State == PermissionState.Granted ? ThemeKeys.Green : ThemeKeys.Orange));
                var n = UiKit.Text(PermissionService.Name(p), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
                n.Margin = new Thickness(8, 0, 0, 0);
                h.Children.Add(n);
                var st = UiKit.Text(status.State == PermissionState.Granted ? "Granted" : "Not granted", 12, FontWeights.Normal, ThemeKeys.TextSecondary);
                st.Margin = new Thickness(8, 0, 0, 0);
                h.Children.Add(st);
                row.Children.Add(h);
                row.Children.Add(UiKit.Text(PermissionService.Explainer(p), 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true).Also(x => x.Margin = new Thickness(22, 3, 0, 0)));
                var users = FeatureCatalog.UsingPermission(p).Where(e => _selection.Contains(e.Id)).Select(e => e.Title).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
                if (users.Count > 0)
                {
                    row.Children.Add(UiKit.Text("Used by " + string.Join(", ", users), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true).Also(x => x.Margin = new Thickness(22, 3, 0, 0)));
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
        disclosure.Children.Add(UiKit.Text("Other permissions", 13.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 10, ThemeKeys.TextSecondary, FontWeights.Bold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        Grid.SetColumn(chevron, 1);
        disclosure.Children.Add(chevron);
        other.Add(disclosure);
        other.AddCaption("Optional. Grant these now or later, when a feature needs them.");
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
        var foot = UiKit.Text("Some features need WinKnife to run as administrator.", 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        foot.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(foot);
        return stack;
    }

    private UIElement Done()
    {
        var stack = new StackPanel();
        stack.Children.Add(Hero(300, 150, "All set!", "WinKnife is already looking after your PC."));
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
        var icon = UiKit.Icon("", 26, ThemeKeys.Accent);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Children.Add(icon);
        var text = UiKit.Text("Look for the WinKnife icon in the system tray (click the ^ if it’s hidden).", 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 340;
        text.Margin = new Thickness(0, 10, 0, 0);
        hint.Children.Add(text);
        stack.Children.Add(hint);
        return stack;
    }
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
