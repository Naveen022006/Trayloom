// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools;

/// <summary>"Taskbar Preview" card of the Taskbar page (spec 02b section 3).</summary>
public sealed class DockPreviewSettingsSection : ISettingsSection
{
    public string? FeatureId => "dock-preview";

    public string PageId => SettingsPages.Dock;

    public string? Anchor => "dockPreview";

    public string? Title => Strings.DockPreviewName;

    public IReadOnlyList<string> Keywords => ["Preview windows", "Open delay", "Panel background", "Close the app", "Keep taskbar visible", "Order windows"];

    public int Order => 0;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        Keys.Register(app);
        var stack = new StackPanel();
        var admin = FormUi.AdminCard(() => WindowUseTracker.ElevatedSeen && !AppIdentity.IsAdmin());
        admin.Margin = new Thickness(0, 0, 0, 20);
        stack.Children.Add(admin);

        var card = new SettingsCard(Strings.DockPreviewName);
        var more = new StackPanel();
        var enabled = app.Get<bool>(Keys.DockPreviewEnabled);

        var unavailable = context.Services.Get<TaskbarTracker>() is { Readable: false } && enabled;
        var enableRow = FormUi.Toggle(app, Keys.DockPreviewEnabled, "", Strings.DockPreviewEnable, unavailable ? Strings.DockUnavailable : Strings.DockPreviewEnableCaption, on =>
        {
            more.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        });
        card.Add(enableRow);
        var beta = UiKit.Text(Strings.Beta, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        card.Add(beta);

        void AddMore(UIElement element)
        {
            if (more.Children.Count > 0 && element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 13, fe.Margin.Right, fe.Margin.Bottom);
            }

            more.Children.Add(element);
        }

        AddMore(FormUi.Toggle(app, Keys.DockCurrentDesktop, "", Strings.DockCurrentDesktop, Strings.DockCurrentDesktopCaption));
        AddMore(new SettingsRow("", Strings.OpenDelay, Strings.OpenDelayCaption, DelayField(app)));
        AddMore(new SettingsRow("", Strings.PanelBackground, Strings.PanelBackgroundCaption,
            FormUi.SliderAccessory(0.4, 1.0, app.Get<double>(Keys.DockOpacity), 0.05, v => $"{(int)Math.Round(v * 100)}%", v => app.Set(Keys.DockOpacity, Math.Round(v, 2)), 44)));
        AddMore(FormUi.Toggle(app, Keys.DockQuitOnX, "", Strings.QuitOnX, Strings.QuitOnXCaption));

        // "More options" disclosure (collapsed on every visit)
        var optionsBody = new StackPanel { Margin = new Thickness(0, 13, 0, 0), Visibility = Visibility.Collapsed };
        optionsBody.Children.Add(FormUi.Toggle(app, Keys.DockKeepVisible, "", Strings.KeepDockVisible, Strings.KeepDockVisibleCaption, enabled: TaskbarSystem.IsAutoHide()));
        var order = FormUi.Toggle(app, Keys.DockOrderByCreation, "", Strings.OrderByCreation, Strings.OrderByCreationCaption);
        order.Margin = new Thickness(0, 13, 0, 0);
        optionsBody.Children.Add(order);
        var chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 9, ThemeKeys.TextSecondary, FontWeights.Bold);
        chevron.Margin = new Thickness(0, 0, 8, 0);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 13, 0, 0) };
        header.Children.Add(chevron);
        header.Children.Add(UiKit.Text(Strings.MoreOptions, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary));
        header.MouseLeftButtonUp += (_, _) =>
        {
            var open = optionsBody.Visibility != Visibility.Visible;
            optionsBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.RenderTransform = new RotateTransform(open ? 90 : 0, 4.5, 6);
        };
        more.Children.Add(header);
        more.Children.Add(optionsBody);
        more.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        card.Add(more);
        stack.Children.Add(card);
        return stack;
    }

    /// <summary>Number field (64 wide) with a stepper (step 50) and "ms", range 200..900.</summary>
    private static FrameworkElement DelayField(IAppSettings app)
    {
        var box = new TextBox { Width = 64, Text = Math.Clamp(app.Get<int>(Keys.DockOpenDelay), 200, 900).ToString(), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 3, 6, 3) };
        void Apply(int value)
        {
            value = Math.Clamp(value, 200, 900);
            box.Text = value.ToString();
            app.Set(Keys.DockOpenDelay, value);
        }

        box.LostFocus += (_, _) => Apply(int.TryParse(box.Text, out var typed) ? typed : app.Get<int>(Keys.DockOpenDelay));
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply(int.TryParse(box.Text, out var typed) ? typed : app.Get<int>(Keys.DockOpenDelay));
            }
        };
        var up = Stepper("", () => Apply((int.TryParse(box.Text, out var v) ? v : 200) + 50));
        var down = Stepper("", () => Apply((int.TryParse(box.Text, out var v) ? v : 200) - 50));
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(box);
        var steppers = new StackPanel { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        steppers.Children.Add(up);
        steppers.Children.Add(down);
        panel.Children.Add(steppers);
        var ms = UiKit.Text("ms", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        ms.Margin = new Thickness(6, 0, 0, 0);
        ms.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(ms);
        return panel;
    }

    private static Button Stepper(string glyph, Action click)
    {
        var button = new Button { Content = UiKit.Icon(glyph, 8, ThemeKeys.TextSecondary, FontWeights.Bold), Width = 20, Height = 12, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>"Taskbar clicks" card of the Taskbar page.</summary>
public sealed class DockClickSettingsSection : ISettingsSection
{
    public string? FeatureId => "dock-click";

    public string PageId => SettingsPages.Dock;

    public string? Anchor => "dockClick";

    public string? Title => Strings.DockClickTitle;

    public IReadOnlyList<string> Keywords => ["Click the taskbar icon", "minimize", "hide", "cycle windows"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        Keys.Register(app);
        var card = new SettingsCard(Strings.DockClickTitle);
        MacSwitch? hide = null;
        MacSwitch? minimize = null;
        minimize = new MacSwitch(app.Get<bool>(Keys.DockClickMinimize), SwitchSize.Regular, on =>
        {
            app.Set(Keys.DockClickMinimize, on);
            if (on && hide?.IsOn == true)
            {
                hide.IsOn = false;
            }
        });
        hide = new MacSwitch(app.Get<bool>(Keys.DockClickHide), SwitchSize.Regular, on =>
        {
            app.Set(Keys.DockClickHide, on);
            if (on && minimize?.IsOn == true)
            {
                minimize.IsOn = false;
            }
        });
        card.Add(new SettingsRow("", Strings.ClickMinimize, Strings.ClickMinimizeCaption, minimize));
        card.Add(new SettingsRow("", Strings.ClickHide, Strings.ClickHideCaption, hide));
        card.Add(FormUi.Toggle(app, Keys.DockClickCycle, "", Strings.ClickCycle, Strings.ClickCycleCaption));
        return card;
    }
}

/// <summary>"Window thumbnails" card of the Taskbar page (shared look with the Switcher page, own keys).</summary>
public sealed class DockThumbnailsSettingsSection : ISettingsSection
{
    public string? FeatureId => "dock-preview";

    public string PageId => SettingsPages.Dock;

    public string? Title => Strings.WindowThumbnails;

    public IReadOnlyList<string> Keywords => ["Preview size", "Minimal previews", "Pause in these apps"];

    public int Order => 30;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        Keys.Register(context.App);
        return ThumbnailsCard.Build(context.App, Keys.PreviewSize, Keys.PreviewExcluded);
    }
}
