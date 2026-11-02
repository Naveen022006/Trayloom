// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Is Windows' own edge snapping on (it uses the same edges)?</summary>
internal static class SnapConflict
{
    public static bool IsWindowsSnapOn()
    {
        try
        {
            var value = new int[1];
            return NativeMethods.SystemParametersInfoW(0x0082, 0, value, 0) && value[0] != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Warning + "Open Multitasking settings" block, re-evaluated when the app becomes active.</summary>
    public static FrameworkElement Build(IAppSettings app)
    {
        var host = new StackPanel();
        void Refresh()
        {
            host.Children.Clear();
            if (!IsWindowsSnapOn())
            {
                return;
            }

            var warning = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = UiKit.Icon("", 12, ThemeKeys.Orange);
            icon.Margin = new Thickness(0, 1, 6, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            warning.Children.Add(icon);
            var text = FormUi.Warning(Strings.EdgeSnapConflict);
            text.MaxWidth = 560;
            warning.Children.Add(text);
            host.Children.Add(warning);
            if (app.Get<bool>(Keys.EdgeSnapEnabled))
            {
                var waiting = UiKit.Text(Strings.EdgeSnapWaiting, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
                waiting.Margin = new Thickness(0, 6, 0, 0);
                host.Children.Add(waiting);
            }

            var open = UiKit.Button(Strings.OpenMultitasking, ButtonKind.Bordered, 11.5, null, () => UiKit.OpenUrl("ms-settings:multitasking"));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 8, 0, 0);
            host.Children.Add(open);
        }

        host.Loaded += (_, _) =>
        {
            Refresh();
            if (Window.GetWindow(host) is { } window)
            {
                window.Activated += (_, _) => Refresh();
            }
        };
        Refresh();
        return host;
    }
}

/// <summary>The Window layout settings page (spec 02b section 4): a grouped form.</summary>
public sealed class WindowLayoutSettingsSection : ISettingsSection
{
    public string? FeatureId => "window-layout";

    public string PageId => SettingsPages.WindowLayout;

    public string? Title => Strings.LayoutPageTitle;

    public IReadOnlyList<string> Keywords =>
        ["Snap windows", "Edge snap", "Gaps", "Window gap", "Screen gap", "Halves", "Thirds", "Sixths", "Corners", "Margin", "Ignore apps", "Pointer layout", "Move and resize"];

    public int Order => 0;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        Keys.Register(app);
        var page = new StackPanel();
        void Add(UIElement element)
        {
            if (page.Children.Count > 0 && element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 20, fe.Margin.Right, fe.Margin.Bottom);
            }

            page.Children.Add(element);
        }

        var service = context.Services.Get<WindowLayoutService>();

        // 1. intro
        var intro = new StackPanel();
        intro.Children.Add(FormUi.Toggle(app, Keys.PanelUtility, null, Strings.ShowInPanel, Strings.LayoutIntro));
        var apis = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var handIcon = UiKit.Icon("", 11, ThemeKeys.TextSecondary);
        handIcon.Margin = new Thickness(0, 0, 6, 0);
        apis.Children.Add(handIcon);
        apis.Children.Add(UiKit.Text(Strings.LayoutApis, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        intro.Children.Add(apis);
        Add(FormUi.Section(null, intro));

        // admin
        var admin = FormUi.AdminCard(() => WindowUseTracker.ElevatedSeen && !AppIdentity.IsAdmin());
        Add(admin);

        // 2. window dragging
        Add(FormUi.Section(Strings.WindowDragging, DraggingContent(app)));

        // 3. ignore apps
        Add(FormUi.Section(Strings.IgnoreApps, new AppBundleList(
            Strings.PauseInApps,
            Strings.IgnoreAppsCaption,
            () => [.. app.Get<List<string>>(Keys.LayoutIgnored) ?? []],
            list => app.Set(Keys.LayoutIgnored, list))));

        // 4. gaps
        var gapItems = new (int, string)[] { (0, "None"), (8, "Tiny (8 px)"), (16, "Small (16 px)"), (32, "Medium (32 px)"), (64, "Large (64 px)"), (128, "Extra large (128 px)") };
        (int, string)[] WithCurrent(int value) => gapItems.Any(g => g.Item1 == value) ? gapItems : [.. gapItems, (value, $"{value} px")];
        var windowGap = app.Get<int>(Keys.WindowGap);
        var screenGap = app.Get<int>(Keys.ScreenGap);
        var gaps = new StackPanel();
        gaps.Children.Add(new PickerRow<int>(null, Strings.WindowGap, null, WithCurrent(windowGap), windowGap, v => app.Set(Keys.WindowGap, v), 170));
        var screenGapRow = new PickerRow<int>(null, Strings.ScreenGap, null, WithCurrent(screenGap), screenGap, v => app.Set(Keys.ScreenGap, v), 170);
        screenGapRow.Margin = new Thickness(0, 13, 0, 0);
        gaps.Children.Add(screenGapRow);
        gaps.Children.Add(Caption(Strings.GapsCaption, 13));
        Add(FormUi.Section(Strings.Gaps, gaps));

        // 5. shortcuts
        var shortcuts = new StackPanel();
        var placements = new StackPanel();
        shortcuts.Children.Add(FormUi.Toggle(app, Keys.LayoutShortcutsEnabled, null, Strings.Shortcuts, Strings.ShortcutsCaption, on => placements.IsEnabled = on));
        shortcuts.Children.Add(Spacer(13));
        var directionalOpts = new StackPanel();
        var directional = FormUi.Toggle(app, Keys.DirectionalEnabled, null, Strings.Directional, Strings.DirectionalCaption, on => directionalOpts.Visibility = on ? Visibility.Visible : Visibility.Collapsed);
        shortcuts.Children.Add(directional);
        if (context.Shortcuts.Role(Keys.DirectionalRole) is not null)
        {
            directionalOpts.Children.Add(ShortcutUi.LabelledRow(context.Shortcuts, Keys.DirectionalRole, null, "Shortcut"));
        }

        directionalOpts.Margin = new Thickness(0, 13, 0, 0);
        directionalOpts.Visibility = app.Get<bool>(Keys.DirectionalEnabled) ? Visibility.Visible : Visibility.Collapsed;
        shortcuts.Children.Add(directionalOpts);
        Add(FormUi.Section(Strings.Shortcuts, shortcuts));

        // 6. pointer to next display
        var pointer = new StackPanel();
        var pointerRow = ShortcutUi.LabelledRow(context.Shortcuts, "pointerNextDisplay", null, "Shortcut", app.Get<bool>(Keys.PointerDisplayEnabled));
        pointerRow.Margin = new Thickness(0, 13, 0, 0);
        pointer.Children.Add(FormUi.Toggle(app, Keys.PointerDisplayEnabled, null, Strings.PointerDisplay, Strings.PointerDisplayCaption, on => pointerRow.IsEnabled = on));
        pointerRow.IsEnabled = app.Get<bool>(Keys.PointerDisplayEnabled);
        pointer.Children.Add(pointerRow);
        Add(FormUi.Section(null, pointer));

        // 7. mirror of the taskbar clicks toggle
        Add(FormUi.Section(null, new FeatureSwitchRow(context.Runtime, context.Shell, "dock-click", null, Strings.ClickCycle, Strings.ClickCycleCaption,
            app.Get<bool>(Keys.DockClickCycle), on => app.Set(Keys.DockClickCycle, on), "Install Taskbar clicks to use this.")));

        // 8. placement sections
        placements.IsEnabled = app.Get<bool>(Keys.LayoutShortcutsEnabled);
        var result = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        result.Visibility = Visibility.Collapsed;
        void ShowResult()
        {
            if (service?.LastResult is not { } r)
            {
                return;
            }

            result.Text = WindowLayoutEngine.Message(r);
            result.SetResourceReference(TextBlock.ForegroundProperty, r is LayoutResult.Done or LayoutResult.Restored ? ThemeKeys.Green : ThemeKeys.Orange);
            result.Visibility = Visibility.Visible;
        }

        if (service is not null)
        {
            page.Loaded += (_, _) => service.ResultChanged += ShowResult;
            page.Unloaded += (_, _) => service.ResultChanged -= ShowResult;
        }

        foreach (var group in new[] { LayoutGroup.Halves, LayoutGroup.Thirds, LayoutGroup.QuarterRows, LayoutGroup.QuarterColumns, LayoutGroup.Sixths, LayoutGroup.Corners, LayoutGroup.Actions })
        {
            var rows = new StackPanel();
            var first = true;
            foreach (var action in LayoutActions.InGroup(group))
            {
                if (!first)
                {
                    rows.Children.Add(Spacer(10));
                }

                first = false;
                rows.Children.Add(ActionRow(context, service, action));
                if (action.Raw == "marginMaximize")
                {
                    var slider = new SettingsRow(null, Strings.MarginPerEdge, null, FormUi.SliderAccessory(0, 25, app.Get<double>(Keys.MarginPercent), 1, v => $"{(int)Math.Round(v)}%", v => app.Set(Keys.MarginPercent, v), 44));
                    slider.Margin = new Thickness(0, 10, 0, 0);
                    rows.Children.Add(slider);
                }
            }

            if (group == LayoutGroup.Halves)
            {
                var repeat = FormUi.Toggle(app, Keys.SideRepeat, null, Strings.RepeatCycles, Strings.RepeatCyclesCaption);
                repeat.Margin = new Thickness(0, 14, 0, 0);
                rows.Children.Add(repeat);
            }

            if (group == LayoutGroup.Actions)
            {
                result.Margin = new Thickness(0, 12, 0, 0);
                rows.Children.Add(result);
            }

            var section = FormUi.Section(LayoutActions.GroupTitle(group), rows);
            placements.Children.Add(section);
            if (group != LayoutGroup.Actions)
            {
                section.Margin = new Thickness(0, 0, 0, 20);
            }
        }

        Add(placements);
        return page;
    }

    private static UIElement Spacer(double height) => new Border { Height = height };

    private static TextBlock Caption(string text, double top)
    {
        var block = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        block.Margin = new Thickness(0, top, 0, 0);
        return block;
    }

    private static FrameworkElement DraggingContent(IAppSettings app)
    {
        var stack = new StackPanel();
        var zonePicker = new EdgeSnapZonePicker(app);
        zonePicker.Margin = new Thickness(0, 13, 0, 0);
        void Enable(bool on)
        {
            zonePicker.IsEnabled = on;
            zonePicker.Opacity = on ? 1 : 0.45;
        }

        stack.Children.Add(FormUi.Toggle(app, Keys.EdgeSnapEnabled, null, Strings.EdgeSnap, null, Enable));
        stack.Children.Add(Caption(Strings.EdgeSnapCaption, 8));
        Enable(app.Get<bool>(Keys.EdgeSnapEnabled));
        stack.Children.Add(zonePicker);
        var conflict = SnapConflict.Build(app);
        if (conflict is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 13, 0, 0);
        }

        stack.Children.Add(conflict);

        var gestureBlock = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };
        var options = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };
        var gestureToggle = FormUi.Toggle(app, Keys.GestureEnabled, null, Strings.MoveResize, null, on => options.Visibility = on ? Visibility.Visible : Visibility.Collapsed);
        gestureBlock.Children.Add(gestureToggle);
        gestureBlock.Children.Add(Caption(Strings.MoveResizeCaption, 8));

        var tokens = GestureModifiers.Parse(app.Get<string>(Keys.GestureModifiers));
        var picker = new Grid();
        var hints = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        void Draw()
        {
            picker.Children.Clear();
            picker.ColumnDefinitions.Clear();
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            picker.Children.Add(UiKit.Text(Strings.KeysToMove, 13, FontWeights.Normal, ThemeKeys.TextSecondary));
            var caps = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var token in new[] { "control", "option", "command" })
            {
                var selected = tokens.Contains(token);
                var last = selected && tokens.Count == 1;
                var captured = token;
                caps.Children.Add(FormUi.Keycap(GestureModifiers.Label(token), selected, !last, () =>
                {
                    if (!tokens.Add(captured))
                    {
                        tokens.Remove(captured);
                    }

                    app.Set(Keys.GestureModifiers, GestureModifiers.Serialize(tokens));
                    Draw();
                }));
            }

            Grid.SetColumn(caps, 1);
            picker.Children.Add(caps);

            hints.Children.Clear();
            var chord = string.Join('+', new[] { "control", "option", "command" }.Where(tokens.Contains).Select(GestureModifiers.Label));
            hints.Children.Add(Hint("", chord, Strings.DragToMove));
            hints.Children.Add(Hint("", chord + "+Shift", Strings.DragToResize));
        }

        Draw();
        options.Children.Add(picker);
        options.Children.Add(hints);
        options.Children.Add(Caption(Strings.ResizeHint, 10));
        var raise = FormUi.Toggle(app, Keys.GestureRaise, null, Strings.BringToFront, null);
        raise.Margin = new Thickness(0, 13, 0, 0);
        options.Children.Add(raise);
        options.Visibility = app.Get<bool>(Keys.GestureEnabled) ? Visibility.Visible : Visibility.Collapsed;
        gestureBlock.Children.Add(options);
        stack.Children.Add(gestureBlock);
        return stack;
    }

    private static FrameworkElement Hint(string glyph, string chord, string text)
    {
        var chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 9, 0) };
        chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(UiKit.Text(chord, 11, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        var icon = UiKit.Icon(glyph, 11, ThemeKeys.Accent, FontWeights.SemiBold);
        icon.Margin = new Thickness(7, 0, 6, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextPrimary));
        chip.Child = row;
        return chip;
    }

    private static FrameworkElement ActionRow(ISettingsContext context, WindowLayoutService? service, LayoutAction action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = FormUi.ActionGlyph(action, 18, 12);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        content.Children.Add(glyph);
        content.Children.Add(UiKit.Text(action.Title, 12, FontWeights.Medium, ThemeKeys.TextPrimary));
        var tryIt = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 12, 4), IsEnabled = service is not null };
        tryIt.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonBordered);
        tryIt.Click += (_, _) => service?.Run(action);
        grid.Children.Add(tryIt);
        var accessory = ShortcutUi.Accessory(context.Shortcuts, action.RoleId, true, true, out var note);
        Grid.SetColumn(accessory, 1);
        grid.Children.Add(accessory);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(note);
        return stack;
    }
}
