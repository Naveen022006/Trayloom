// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools;

/// <summary>The Quit on close page (spec 02b section 5): toggle, how it works, exceptions, notification-area apps to quit.</summary>
public sealed class QuitOnCloseSettingsSection : ISettingsSection
{
    public string? FeatureId => "auto-quit";

    public string PageId => SettingsPages.QuitOnClose;

    public string? Title => Strings.QuitOnCloseTitle;

    public IReadOnlyList<string> Keywords => ["Exceptions", "How it works", "last window", "notification area", "tray"];

    public int Order => 0;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        Keys.Register(app);
        var page = new StackPanel();
        var enabled = app.Get<bool>(Keys.AutoQuitEnabled);
        var bodies = new List<UIElement>();

        void Add(UIElement element)
        {
            if (page.Children.Count > 0 && element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 20, fe.Margin.Right, fe.Margin.Bottom);
            }

            page.Children.Add(element);
        }

        // 1. toggle + active badge
        var active = FormUi.ActiveBadge();
        active.Margin = new Thickness(0, 10, 0, 0);
        void UpdateActive() => active.Visibility = app.Get<bool>(Keys.AutoQuitEnabled) && context.Runtime.IsRunning("auto-quit") ? Visibility.Visible : Visibility.Collapsed;
        var first = new StackPanel();
        first.Children.Add(FormUi.Toggle(app, Keys.AutoQuitEnabled, null, Strings.AutoQuitEnable, Strings.AutoQuitEnableCaption, on =>
        {
            foreach (var body in bodies)
            {
                body.IsEnabled = on;
                body.Opacity = on ? 1 : 0.45;
            }

            UpdateActive();
        }));
        first.Children.Add(active);
        UpdateActive();
        Add(FormUi.Section(null, first));

        var admin = FormUi.AdminCard(() => enabled && WindowUseTracker.ElevatedSeen && !AppIdentity.IsAdmin());
        Add(admin);

        // 2. how it works
        var how = new StackPanel();
        how.Children.Add(Bullet("", Strings.AutoQuitStep1, ThemeKeys.TextPrimary));
        var step2 = Bullet("", Strings.AutoQuitStep2, ThemeKeys.TextPrimary);
        step2.Margin = new Thickness(0, 8, 0, 0);
        how.Children.Add(step2);
        var predictable = UiKit.Text(Strings.AutoQuitPredictable, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        predictable.Margin = new Thickness(0, 10, 0, 0);
        how.Children.Add(predictable);
        var ongoing = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        ongoing.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ongoing.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var warn = UiKit.Icon("", 11, ThemeKeys.Orange);
        warn.Margin = new Thickness(0, 1, 6, 0);
        warn.VerticalAlignment = VerticalAlignment.Top;
        ongoing.Children.Add(warn);
        var ongoingText = UiKit.Text(Strings.AutoQuitOngoing, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        Grid.SetColumn(ongoingText, 1);
        ongoing.Children.Add(ongoingText);
        how.Children.Add(ongoing);
        Add(FormUi.Section(Strings.HowItWorks, how));

        // 3. exceptions
        var exceptions = ExceptionsBody(app);
        bodies.Add(exceptions);
        Add(FormUi.Section(Strings.Exceptions, exceptions));

        // 4. tray apps
        var tray = new AppBundleList(
            Strings.TrayAppsToQuit,
            Strings.TrayAppsCaption,
            () => [.. app.Get<List<string>>(Keys.AutoQuitTrayApps) ?? []],
            list => app.Set(Keys.AutoQuitTrayApps, list),
            loader: TrayCandidates,
            allowBrowse: false,
            emptyNote: Strings.NoTrayApps);
        bodies.Add(tray);
        Add(FormUi.Section(null, tray));

        foreach (var body in bodies)
        {
            body.IsEnabled = enabled;
            body.Opacity = enabled ? 1 : 0.45;
        }

        return page;
    }

    private static IReadOnlyList<AppEntry> TrayCandidates()
    {
        var entries = new List<AppEntry>();
        var running = TrayApps.RunningPaths();
        foreach (var path in TrayApps.Known().Where(p => File.Exists(p) && running.ContainsKey(p) && !AutoQuitRules.IsMandatory(p, 0)))
        {
            entries.Add(new AppEntry(path, AppIdentity.DisplayName(path), Path.GetDirectoryName(path) ?? path, AppIdentity.IconOf(path)));
        }

        return [.. entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static FrameworkElement Bullet(string glyph, string text, string brush)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = UiKit.Icon(glyph, 12, ThemeKeys.Accent, FontWeights.SemiBold);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(icon);
        var label = UiKit.Text(text, 12.5, FontWeights.Normal, brush, wrap: true);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        return grid;
    }

    private static FrameworkElement ExceptionsBody(IAppSettings app)
    {
        var stack = new StackPanel();
        void Build()
        {
            stack.Children.Clear();
            var user = (app.Get<List<string>>(Keys.AutoQuitExceptions) ?? []).Where(u => !AutoQuitRules.IsListed(u)).ToList();
            var rows = AutoQuitRules.Listed.Select(l => (Identity: l, Locked: true)).Concat(user.Select(u => (Identity: u, Locked: false)))
                .OrderBy(r => AppIdentity.DisplayName(r.Identity), StringComparer.CurrentCultureIgnoreCase).ToList();
            if (rows.Count == 0)
            {
                stack.Children.Add(UiKit.Text(Strings.NoExceptions, 12, FontWeights.Normal, ThemeKeys.TextSecondary));
            }

            foreach (var (identity, locked) in rows)
            {
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var path = identity.Contains('\\') ? identity : null;
                var icon = AppIdentity.IconOf(path);
                if (icon is not null)
                {
                    grid.Children.Add(new Image { Source = icon, Width = 20, Height = 20 });
                }

                var name = UiKit.Text(AppIdentity.DisplayName(identity), 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
                name.ToolTip = identity;
                Grid.SetColumn(name, 2);
                grid.Children.Add(name);
                FrameworkElement trailing;
                if (locked)
                {
                    trailing = UiKit.Icon("", 12, ThemeKeys.TextTertiary);
                }
                else
                {
                    var remove = new Button { Content = UiKit.Icon(Glyphs.CloseCircle, 14, ThemeKeys.TextSecondary), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2), Cursor = Cursors.Hand, ToolTip = Strings.Remove };
                    var captured = identity;
                    remove.Click += (_, _) =>
                    {
                        var list = app.Get<List<string>>(Keys.AutoQuitExceptions) ?? [];
                        list.RemoveAll(x => string.Equals(x, captured, StringComparison.OrdinalIgnoreCase));
                        app.Set(Keys.AutoQuitExceptions, list);
                        Build();
                    };
                    trailing = remove;
                }

                Grid.SetColumn(trailing, 3);
                grid.Children.Add(trailing);
                stack.Children.Add(grid);
            }

            var add = UiKit.Button(Strings.AddApp, ButtonKind.Bordered, 11.5, Glyphs.Add, () =>
            {
                var existing = app.Get<List<string>>(Keys.AutoQuitExceptions) ?? [];
                var picker = new AppPickerWindow(null, existing, true);
                if (Window.GetWindow(stack) is { } owner)
                {
                    picker.Owner = owner;
                    picker.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }

                if (picker.ShowDialog() == true && picker.Result is { Length: > 0 } picked)
                {
                    existing = app.Get<List<string>>(Keys.AutoQuitExceptions) ?? [];
                    if (!existing.Any(x => string.Equals(x, picked, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Add(picked);
                        app.Set(Keys.AutoQuitExceptions, existing);
                    }

                    Build();
                }
            });
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(add);
            var caption = UiKit.Text(Strings.ExceptionsCaption, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            caption.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(caption);
        }

        Build();
        return stack;
    }
}
