// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools;

/// <summary>The Switcher settings page (spec 02b section 2): the layout chooser, shortcuts, options, rules and thumbnails.</summary>
public sealed class SwitcherSettingsSection : ISettingsSection
{
    public string? FeatureId => "switcher";

    public string PageId => SettingsPages.Switcher;

    public string? Anchor => "switcher";

    public string? Title => Strings.SwitcherSection;

    public IReadOnlyList<string> Keywords =>
    [
        "Layout", "Window previews", "Large icons", "Simple list", "Alt+Tab", "Appearance delay", "Maximum columns", "Instant selection",
        "Minimized windows", "Rules by app", "Thumbnails", "Preview size", "Minimal previews",
    ];

    public int Order => 0;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        Keys.Register(app);
        var page = new StackPanel();
        page.Children.Add(SwitcherCard(context));
        var options = OptionsCard(context);
        options.Margin = new Thickness(0, 20, 0, 0);
        page.Children.Add(options);
        var thumbs = ThumbnailsCard.Build(app, Keys.SwitcherPreviewSize, Keys.SwitcherExcluded);
        thumbs.Margin = new Thickness(0, 20, 0, 0);
        page.Children.Add(thumbs);
        return page;
    }

    // ---- card 1 ---------------------------------------------------------------------------------------------

    private static FrameworkElement SwitcherCard(ISettingsContext context)
    {
        var app = context.App;
        var card = new SettingsCard(Strings.SwitcherSection);
        var body = new StackPanel();

        card.Add(FormUi.Toggle(app, Keys.SwitcherEnabled, "", Strings.SwitcherUse, Strings.SwitcherUseCaption, on =>
        {
            body.IsEnabled = on;
            body.Opacity = on ? 1 : 0.45;
        }));
        body.IsEnabled = app.Get<bool>(Keys.SwitcherEnabled);
        body.Opacity = body.IsEnabled ? 1 : 0.45;
        card.Add(body);

        void AddBody(UIElement element)
        {
            if (body.Children.Count > 0 && element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 13, fe.Margin.Right, fe.Margin.Bottom);
            }

            body.Children.Add(element);
        }

        AddBody(UiKit.Text(Strings.Layout, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary));
        var tiles = new UniformGrid { Columns = 3 };
        void DrawTiles()
        {
            tiles.Children.Clear();
            var current = Current(app);
            var defs = new (SwitchLayout Layout, string Title, string Caption, Func<FrameworkElement> Sketch)[]
            {
                (SwitchLayout.Windows, Strings.LayoutWindows, Strings.LayoutWindowsCaption, SketchWindows),
                (SwitchLayout.Icons, Strings.LayoutIcons, Strings.LayoutIconsCaption, SketchIcons),
                (SwitchLayout.Simple, Strings.LayoutSimple, Strings.LayoutSimpleCaption, SketchSimple),
            };
            for (var i = 0; i < defs.Length; i++)
            {
                var (layout, title, caption, sketch) = defs[i];
                var selected = layout == current;
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10),
                    Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                if (selected)
                {
                    tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
                    tile.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
                }
                else
                {
                    tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                    tile.BorderBrush = Brushes.Transparent;
                }

                var stack = new StackPanel();
                var box = new Border { Height = 64, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(209, 0, 0, 0)), Child = sketch() };
                stack.Children.Add(box);
                var titleText = UiKit.Text(title, 12, FontWeights.SemiBold, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
                titleText.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(titleText);
                var captionText = UiKit.Text(caption, 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
                captionText.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(captionText);
                tile.Child = stack;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    app.Set(Keys.SwitcherSimple, layout == SwitchLayout.Simple);
                    app.Set(Keys.SwitcherIconRow, layout == SwitchLayout.Icons);
                    DrawTiles();
                    LayoutChanged?.Invoke();
                };
                tiles.Children.Add(tile);
            }
        }

        DrawTiles();
        AddBody(tiles);
        AddBody(UiKit.Divider());

        var apps = ShortcutUi.LabelledRow(context.Shortcuts, "switcher", "", Strings.ShortcutApps);
        AddBody(apps);
        AddBody(ShortcutUi.LabelledRow(context.Shortcuts, "switcherWindow", "", Strings.ShortcutWindows));
        var windowCaption = UiKit.Text(Strings.WindowShortcutCaption, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        AddBody(windowCaption);

        var allShortcut = new StackPanel();
        AddBody(FormUi.Toggle(app, Keys.SwitcherAllWindowsEnabled, "", Strings.AllWindows, Strings.AllWindowsCaption, on =>
        {
            allShortcut.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }));
        allShortcut.Children.Add(ShortcutUi.LabelledRow(context.Shortcuts, "switcherAllWindows", null, string.Empty));
        allShortcut.Margin = new Thickness(0, 13, 0, 0);
        allShortcut.Visibility = app.Get<bool>(Keys.SwitcherAllWindowsEnabled) ? Visibility.Visible : Visibility.Collapsed;
        body.Children.Add(allShortcut);

        var warning = FormUi.Warning(Strings.TakeOverWarning, SettingsRow.TextInset);
        void UpdateWarning()
        {
            var apps2 = context.Shortcuts.GetHotkey("switcher");
            var show = !app.Get<bool>(Keys.SwitcherTakeOver) && apps2 is { Key: Key.Tab, Modifiers: HotkeyModifiers.Alt };
            warning.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        AddBody(FormUi.Toggle(app, Keys.SwitcherTakeOver, "", Strings.TakeOver, Strings.TakeOverCaption, _ =>
        {
            UpdateWarning();
            TakeOverChanged?.Invoke();
        }));
        warning.Margin = new Thickness(SettingsRow.TextInset, 6, 0, 0);
        UpdateWarning();
        body.Children.Add(warning);

        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 13, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        hint.Text = string.Format(Strings.UsageHintFormat, context.Shortcuts.GetHotkey("switcher")?.ToString() ?? "Alt+Tab");
        body.Children.Add(hint);
        return card;
    }

    private static event Action? LayoutChanged;

    private static event Action? TakeOverChanged;

    private static SwitchLayout Current(IAppSettings app) =>
        app.Get<bool>(Keys.SwitcherSimple) ? SwitchLayout.Simple : app.Get<bool>(Keys.SwitcherIconRow) ? SwitchLayout.Icons : SwitchLayout.Windows;

    private static FrameworkElement SketchWindows()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 3; i++)
        {
            var window = new Border
            {
                Width = 34,
                Height = 26,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(i == 0 ? (byte)230 : (byte)102, 255, 255, 255)),
            };
            if (i == 0)
            {
                window.BorderThickness = new Thickness(1.5);
                window.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }

            window.Child = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(89, 0, 0, 0)), CornerRadius = new CornerRadius(2, 2, 0, 0) };
            row.Children.Add(window);
        }

        return row;
    }

    private static FrameworkElement SketchIcons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            var column = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0) };
            column.Children.Add(new Border { Width = 26, Height = 18, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(i == 0 ? (byte)150 : (byte)0, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 5) });
            var icon = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center };
            if (i == 0)
            {
                icon.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            }
            else
            {
                icon.Background = new SolidColorBrush(Color.FromArgb(89, 255, 255, 255));
            }

            column.Children.Add(icon);
            row.Children.Add(column);
        }

        return row;
    }

    private static FrameworkElement SketchSimple()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new Border { Width = 44, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 8) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            var icon = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
            if (i == 0)
            {
                icon.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
            }
            else
            {
                icon.Background = new SolidColorBrush(Color.FromArgb(89, 255, 255, 255));
            }

            row.Children.Add(icon);
        }

        stack.Children.Add(row);
        return stack;
    }

    // ---- card 2 ---------------------------------------------------------------------------------------------

    private static FrameworkElement OptionsCard(ISettingsContext context)
    {
        var app = context.App;
        var card = new SettingsCard(Strings.Options);
        var body = new StackPanel();
        var enabled = app.Get<bool>(Keys.SwitcherEnabled);

        void AddBody(UIElement element)
        {
            if (body.Children.Count > 0 && element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 13, fe.Margin.Right, fe.Margin.Bottom);
            }

            body.Children.Add(element);
        }

        AddBody(new SettingsRow("", Strings.AppearanceDelay, Strings.AppearanceDelayCaption,
            FormUi.SliderAccessory(0, 500, app.Get<int>(Keys.SwitcherDelay), 25, v => Loc.F("{0} ms", (int)v), v => app.Set(Keys.SwitcherDelay, (int)v))));
        AddBody(new SettingsRow("", Strings.MaxColumns, Strings.MaxColumnsCaption,
            FormUi.SliderAccessory(1, 10, Math.Max(1, app.Get<int>(Keys.SwitcherMaxColumns)), 1, v => v <= 1 ? Loc.T("Auto") : ((int)v).ToString(), v => app.Set(Keys.SwitcherMaxColumns, v <= 1 ? 0 : (int)v))));
        AddBody(FormUi.Toggle(app, Keys.SwitcherInstant, "", Strings.InstantSelection, Strings.InstantSelectionCaption));
        AddBody(FormUi.Toggle(app, Keys.SwitcherSearchPin, "", Strings.PinSearch, Strings.PinSearchCaption));
        var hintsRow = FormUi.Toggle(app, Keys.SwitcherHints, "", Strings.ShortcutHints, Strings.ShortcutHintsCaption);
        hintsRow.Visibility = Current(app) == SwitchLayout.Windows ? Visibility.Collapsed : Visibility.Visible;
        AddBody(hintsRow);
        Action layoutHandler = () => hintsRow.Visibility = Current(app) == SwitchLayout.Windows ? Visibility.Collapsed : Visibility.Visible;
        LayoutChanged += layoutHandler;
        card.Unloaded += (_, _) => LayoutChanged -= layoutHandler;
        AddBody(FormUi.Toggle(app, Keys.SwitcherMergeTabs, "", Strings.MergeTabs, Strings.MergeTabsCaption));
        AddBody(FormUi.Toggle(app, Keys.SwitcherFullscreen, "", Strings.FullscreenWindows, null));
        AddBody(UiKit.Divider());

        // minimized windows
        var treat = FormUi.Toggle(app, Keys.SwitcherTreatHidden, "", Strings.TreatHidden, null);
        treat.Visibility = (app.Get<string>(Keys.SwitcherMinimized) ?? "normal") == "normal" ? Visibility.Collapsed : Visibility.Visible;
        var minimizedHost = new StackPanel();
        minimizedHost.Children.Add(new SettingsRow("", Strings.MinimizedWindows, null));
        minimizedHost.Children.Add(FormUi.ChipRow(
            [("normal", Strings.MinNormal), ("end", Strings.MinEnd), ("hidden", Strings.MinHide)],
            () => app.Get<string>(Keys.SwitcherMinimized) ?? "normal",
            v =>
            {
                app.Set(Keys.SwitcherMinimized, v);
                treat.Visibility = v == "normal" ? Visibility.Collapsed : Visibility.Visible;
            },
            SettingsRow.TextInset));
        minimizedHost.Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(SettingsRow.TextInset, 8, 0, 0));
        AddBody(minimizedHost);
        AddBody(treat);

        var showOn = new StackPanel();
        showOn.Children.Add(new SettingsRow("", Strings.ShowOn, Strings.ShowOnCaption));
        var showChips = FormUi.ChipRow(
            [("pointer", Strings.OnPointer), ("menuBar", Strings.OnTaskbar), ("activeWindow", Strings.OnActive)],
            () => app.Get<string>(Keys.SwitcherPlacement) ?? "pointer",
            v => app.Set(Keys.SwitcherPlacement, v),
            SettingsRow.TextInset);
        showChips.Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0);
        showOn.Children.Add(showChips);
        AddBody(showOn);
        AddBody(FormUi.Toggle(app, Keys.SwitcherCurrentDisplay, "", Strings.CurrentDisplayOnly, Strings.CurrentDisplayOnlyCaption));
        AddBody(FormUi.Toggle(app, Keys.SwitcherCurrentDesktop, "", Strings.CurrentDesktopOnly, Strings.CurrentDesktopOnlyCaption));
        AddBody(UiKit.Divider());

        // apps with no open window
        var windowless = new StackPanel();
        windowless.Children.Add(new SettingsRow("", Strings.WindowlessApps, Strings.WindowlessAppsCaption));
        var locked = UiKit.Text(string.Format(Strings.WindowlessLockedFormat, Strings.TakeOver), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        locked.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        var chips = new Border();
        void DrawWindowless()
        {
            var takeOver = app.Get<bool>(Keys.SwitcherTakeOver);
            var row = FormUi.ChipRow(
                [("off", Strings.WindowlessOff), ("finder", Strings.WindowlessExplorer), ("all", Strings.WindowlessAll)],
                () => takeOver ? "all" : app.Get<string>(Keys.SwitcherWindowless) ?? "finder",
                v => app.Set(Keys.SwitcherWindowless, v),
                SettingsRow.TextInset,
                enabled: !takeOver);
            row.Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0);
            chips.Child = row;
            locked.Visibility = takeOver ? Visibility.Visible : Visibility.Collapsed;
        }

        DrawWindowless();
        Action takeHandler = DrawWindowless;
        TakeOverChanged += takeHandler;
        card.Unloaded += (_, _) => TakeOverChanged -= takeHandler;
        windowless.Children.Add(chips);
        windowless.Children.Add(locked);
        AddBody(windowless);

        body.IsEnabled = enabled;
        body.Opacity = enabled ? 1 : 0.45;
        card.Add(body);

        // the rules list stays editable even when the switcher is off
        card.Add(new AppBundleList(
            Strings.RulesByApp,
            Strings.RulesCaption,
            () => [.. (app.Get<Dictionary<string, string>>(Keys.SwitcherRules) ?? []).Keys],
            list =>
            {
                var rules = new Dictionary<string, string>(app.Get<Dictionary<string, string>>(Keys.SwitcherRules) ?? []);
                foreach (var key in rules.Keys.Where(k => !list.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                {
                    rules.Remove(key);
                }

                foreach (var key in list.Where(k => !rules.ContainsKey(k)))
                {
                    rules[key] = "showWithoutWindows";
                }

                app.Set(Keys.SwitcherRules, rules);
            },
            accessory: identity => RulePicker(app, identity)));
        return card;
    }

    private static UIElement RulePicker(IAppSettings app, string identity)
    {
        var items = new (string Value, string Label)[] { ("showWithoutWindows", Strings.RuleShow), ("windowsOnly", Strings.RuleWindowsOnly), ("hidden", Strings.RuleNever) };
        var rules = app.Get<Dictionary<string, string>>(Keys.SwitcherRules) ?? [];
        var current = rules.TryGetValue(identity, out var rule) ? rule : "windowsOnly";
        var combo = new ComboBox { MinWidth = 170, ItemsSource = items.Select(i => i.Label).ToList(), ToolTip = Strings.SwitcherBehavior };
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(items, i => i.Value == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            var updated = new Dictionary<string, string>(app.Get<Dictionary<string, string>>(Keys.SwitcherRules) ?? []) { [identity] = items[combo.SelectedIndex].Value };
            app.Set(Keys.SwitcherRules, updated);
        };
        return combo;
    }
}
