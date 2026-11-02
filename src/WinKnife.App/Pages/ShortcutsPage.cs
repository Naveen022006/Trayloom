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

/// <summary>The Keyboard shortcuts page (spec 02a section 6): every registered shortcut role with status and conflicts.</summary>
internal static class ShortcutsPage
{
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal) { "Tools|Screen capture" };

    private static readonly FeatureGroup[] GroupOrder =
    [
        FeatureGroup.WindowsTaskbar, FeatureGroup.MouseKeyboard, FeatureGroup.ClipboardFiles, FeatureGroup.Sound,
        FeatureGroup.EnergyDisplay, FeatureGroup.Tools, FeatureGroup.Island,
    ];

    public static FrameworkElement Build(ShellServices s, Action rebuild)
    {
        var stack = new StackPanel();
        stack.AddSpaced(new SettingsPageHeader("Keyboard shortcuts", "Edit every global shortcut from the features installed on this PC. Inactive shortcuts stay saved but do not run."), 20);

        var roles = s.Shortcuts.Roles.Where(r => r.FeatureId is null || s.Runtime.IsAvailable(r.FeatureId)).ToList();
        foreach (var group in GroupOrder)
        {
            var inGroup = roles.Where(r => r.Group == group).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            var card = new SettingsCard(group == FeatureGroup.Island ? "Dynamic Island" : group.DisplayName());
            var first = true;
            foreach (var chunk in Chunks(inGroup))
            {
                if (!first)
                {
                    card.AddDivider();
                }

                first = false;
                if (chunk.Key is null)
                {
                    card.Add(RoleRow(s, chunk.First(), rebuild));
                    continue;
                }

                var key = $"{group}|{chunk.Key}";
                var isOpen = Expanded.Contains(key);
                card.Add(SectionHeader(s, chunk.Key, chunk.ToList(), isOpen, () =>
                {
                    if (!Expanded.Remove(key))
                    {
                        Expanded.Add(key);
                    }

                    rebuild();
                }));
                if (isOpen)
                {
                    foreach (var role in chunk)
                    {
                        var host = RoleRow(s, role, rebuild);
                        host.Margin = new Thickness(25, 0, 0, 0);
                        card.Add(host);
                    }
                }
            }

            stack.AddSpaced(card, 20);
        }

        if (s.Runtime.IsAvailable("command-bar"))
        {
            var card = new SettingsCard();
            card.Add(new SettingsRow("", "App shortcuts", "Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here.", UiKit.Button("Open Command Bar settings", ButtonKind.Bordered, 12, onClick: () => s.Shell.OpenSettings(SettingsPages.CommandBar))));
            stack.AddSpaced(card, 20);
        }

        return stack;
    }

    /// <summary>Groups consecutive roles by <see cref="ShortcutRole.Section"/> (null section = single rows).</summary>
    private static IEnumerable<IGrouping<string?, ShortcutRole>> Chunks(List<ShortcutRole> roles)
    {
        var bySection = roles.Where(r => r.Section is not null).GroupBy(r => r.Section).ToList();
        foreach (var plain in roles.Where(r => r.Section is null))
        {
            yield return new SingleGroup(null, plain);
        }

        foreach (var g in bySection)
        {
            yield return g;
        }
    }

    private sealed class SingleGroup(string? key, ShortcutRole role) : IGrouping<string?, ShortcutRole>
    {
        public string? Key { get; } = key;

        public IEnumerator<ShortcutRole> GetEnumerator() => new List<ShortcutRole> { role }.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static UIElement SectionHeader(ShellServices s, string title, List<ShortcutRole> roles, bool open, Action toggle)
    {
        var active = roles.Any(r => s.Shortcuts.GetState(r.Id).IsActive);
        var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Label(title, null, active, true));
        var count = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        count.Child = UiKit.Text(roles.Count.ToString(), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        var chevron = UiKit.Icon(open ? Glyphs.ChevronDownSmall : Glyphs.ChevronRightSmall, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        grid.MouseLeftButtonUp += (_, _) => toggle();
        return grid;
    }

    private static FrameworkElement Label(string title, string? context, bool active, bool showStatus)
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(UiKit.Text(title, 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        if (showStatus)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            if (context is not null)
            {
                line.Children.Add(UiKit.Text(context + "  •  ", 11, FontWeights.Normal, ThemeKeys.TextSecondary));
            }

            var dot = new Ellipse { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, active ? ThemeKeys.Green : ThemeKeys.TextTertiary);
            line.Children.Add(dot);
            var status = UiKit.Text(active ? "Active" : "Inactive", 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            status.Margin = new Thickness(5, 0, 0, 0);
            line.Children.Add(status);
            col.Children.Add(line);
        }

        return col;
    }

    private static FrameworkElement RoleRow(ShellServices s, ShortcutRole role, Action rebuild)
    {
        var state = s.Shortcuts.GetState(role.Id);
        var stack = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var feature = role.FeatureId is null ? null : FeatureCatalog.Get(role.FeatureId)?.Title;
        grid.Children.Add(Label(role.Title, feature is not null && feature != role.Title && role.Section is null ? feature : null, state.IsActive, true));

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var error = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        error.Visibility = Visibility.Collapsed;
        if (role.ReadOnly)
        {
            right.Children.Add(UiKit.Text(state.Configured?.ToString() ?? "None", 12, FontWeights.Normal, ThemeKeys.TextSecondary));
            var manage = UiKit.Button("Manage the menu", ButtonKind.Bordered, 12, onClick: () => s.Shell.OpenSettings(SettingsPages.RadialMenu));
            manage.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(manage);
        }
        else
        {
            var recorder = new HotkeyRecorder { Hotkey = state.Configured, MinWidth = 150, Width = 150 };
            recorder.HotkeyChanged += (_, hotkey) =>
            {
                var result = s.Shortcuts.TrySet(role.Id, hotkey);
                if (!result.Ok)
                {
                    error.Text = result.Error ?? "This shortcut could not be set.";
                    error.Visibility = Visibility.Visible;
                    recorder.Hotkey = s.Shortcuts.GetHotkey(role.Id);
                    return;
                }

                rebuild();
            };
            right.Children.Add(recorder);
            var clear = new Button { Content = UiKit.Icon(Glyphs.CloseCircle, 12, ThemeKeys.TextSecondary), ToolTip = "Remove shortcut", IsEnabled = state.Configured is not null, Margin = new Thickness(6, 0, 0, 0) };
            clear.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            clear.Click += (_, _) =>
            {
                s.Shortcuts.TrySet(role.Id, null);
                rebuild();
            };
            right.Children.Add(clear);
            var reset = UiKit.Button("Reset", ButtonKind.Bordered, 12, onClick: () =>
            {
                s.Shortcuts.Reset(role.Id);
                rebuild();
            });
            reset.IsEnabled = !s.Shortcuts.IsDefault(role.Id);
            reset.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(reset);
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        stack.Children.Add(grid);
        if (state.Problem is { } problem)
        {
            stack.Children.Add(UiKit.Text(problem, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
        }

        stack.Children.Add(error);
        return stack;
    }
}
