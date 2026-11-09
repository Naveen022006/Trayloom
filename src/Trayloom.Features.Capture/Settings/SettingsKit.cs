// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>Row builders for the Screen capture page (toggles, shortcut rows, folder row, disclosure).</summary>
internal static class Sk
{
    public static SettingsRow Toggle(SettingsCard card, IAppSettings app, string title, string key, string? caption = null, Action<bool>? changed = null, bool enabled = true, bool? shownValue = null)
    {
        var sw = new MacSwitch(shownValue ?? app.Get<bool>(key), SwitchSize.Regular, on =>
        {
            app.Set(key, on);
            changed?.Invoke(on);
        })
        {
            IsEnabled = enabled,
        };
        var row = new SettingsRow(null, title, caption, sw);
        card.Add(row);
        return row;
    }

    public static TextBlock Warning(string text)
    {
        var t = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        t.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        return t;
    }

    /// <summary>Toggle (feature shortcut) plus its recorder row and the orange rejection caption.</summary>
    public static void ShortcutToggle(SettingsCard card, ISettingsContext c, string title, string enabledKey, string roleId, string? caption = null, Action<bool>? changed = null)
    {
        var app = c.App;
        var holder = new StackPanel();
        void Build()
        {
            holder.Children.Clear();
            holder.Children.Add(ShortcutRowOnly(c, roleId, app.Get<bool>(enabledKey)));
        }

        Toggle(card, app, title, enabledKey, caption, on =>
        {
            Build();
            changed?.Invoke(on);
        });
        Build();
        card.Add(holder);
    }

    public static UIElement ShortcutRowOnly(ISettingsContext c, string roleId, bool enabled)
    {
        var host = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(Loc.T("Shortcut"), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var warn = Warning(string.Empty);
        warn.Margin = new Thickness(0, 6, 0, 0);
        var state = c.Shortcuts.GetState(roleId);
        if (enabled && !string.IsNullOrEmpty(state.Problem))
        {
            warn.Text = state.Problem;
            warn.Visibility = Visibility.Visible;
        }

        var recorder = new HotkeyRecorder { Width = 128, Hotkey = c.Shortcuts.GetHotkey(roleId), IsEnabled = enabled };
        var reset = UiKit.Button(Strings.Reset, ButtonKind.Bordered, 12);
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.IsEnabled = enabled && !c.Shortcuts.IsDefault(roleId);
        recorder.HotkeyChanged += (_, hotkey) =>
        {
            var result = c.Shortcuts.TrySet(roleId, hotkey);
            if (!result.Ok)
            {
                warn.Text = result.Error ?? Strings.ShortcutRejected;
                warn.Visibility = Visibility.Visible;
                recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
                return;
            }

            var s = c.Shortcuts.GetState(roleId);
            warn.Text = s.Problem ?? string.Empty;
            warn.Visibility = string.IsNullOrEmpty(s.Problem) ? Visibility.Collapsed : Visibility.Visible;
            reset.IsEnabled = enabled && !c.Shortcuts.IsDefault(roleId);
        };
        reset.Click += (_, _) =>
        {
            c.Shortcuts.Reset(roleId);
            recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            warn.Visibility = Visibility.Collapsed;
            reset.IsEnabled = false;
        };
        Grid.SetColumn(recorder, 1);
        Grid.SetColumn(reset, 2);
        grid.Children.Add(recorder);
        grid.Children.Add(reset);
        host.Children.Add(grid);
        host.Children.Add(warn);
        host.Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0);
        return host;
    }

    public static UIElement Segmented(string title, IReadOnlyList<string> items, int selected, Action<int> changed)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(title, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var seg = new SegmentedControl(items, Math.Max(0, selected), false, 12);
        seg.SelectionChanged += changed;
        Grid.SetColumn(seg, 1);
        grid.Children.Add(seg);
        return grid;
    }

    public static UIElement Picker(string title, IReadOnlyList<(string Value, string Label)> items, string selected, Action<string> changed, string? caption = null, double width = 170) =>
        new PickerRow<string>(null, title, caption, items, selected, changed, width);

    public static UIElement Caption(string text, double left = 0)
    {
        var t = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        t.Margin = new Thickness(left, 0, 0, 0);
        return t;
    }

    /// <summary>"Save to | folder | x | Choose..." row.</summary>
    public static UIElement FolderRow(IAppSettings app, string key)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(Strings.SaveTo, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var name = UiKit.Text(Output.FolderDisplayName(app.Get<string>(key)), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        name.HorizontalAlignment = HorizontalAlignment.Right;
        name.Margin = new Thickness(12, 0, 8, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var clear = new Button { Content = UiKit.Icon(G.CloseCircle, 12, ThemeKeys.TextSecondary), ToolTip = Strings.Reset, Padding = new Thickness(2), Visibility = string.IsNullOrEmpty(app.Get<string>(key)) ? Visibility.Collapsed : Visibility.Visible };
        clear.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        clear.Click += (_, _) =>
        {
            app.Set(key, string.Empty);
            name.Text = Output.FolderDisplayName(string.Empty);
            clear.Visibility = Visibility.Collapsed;
        };
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);
        var choose = UiKit.Button(Strings.Choose, ButtonKind.Bordered, 12, null, () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Output.BaseFolder(app.Get<string>(key)) };
            if (dlg.ShowDialog() == true)
            {
                app.Set(key, dlg.FolderName);
                name.Text = Output.FolderDisplayName(dlg.FolderName);
                clear.Visibility = Visibility.Visible;
            }
        });
        choose.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(choose, 3);
        grid.Children.Add(choose);
        return grid;
    }

    /// <summary>A collapsible group; the open state is not persisted.</summary>
    public static UIElement Disclosure(string title, UIElement content)
    {
        var host = new StackPanel();
        var open = false;
        var chevron = UiKit.Icon(G.ChevronRight, 10, ThemeKeys.TextSecondary, FontWeights.Bold);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        head.Children.Add(chevron);
        var t = UiKit.Text(title, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        t.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(t);
        var body = new Border { Child = content, Margin = new Thickness(SettingsRow.TextInset - 10, 10, 0, 0), Visibility = Visibility.Collapsed };
        head.MouseLeftButtonUp += (_, _) =>
        {
            open = !open;
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? G.ChevronDown : G.ChevronRight;
        };
        host.Children.Add(head);
        host.Children.Add(body);
        return host;
    }

    public static StackPanel Spaced(double spacing, params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children)
        {
            if (c is FrameworkElement fe && p.Children.Count > 0)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
            }

            p.Children.Add(c);
        }

        return p;
    }
}

/// <summary>Which capture tool the Screen capture page shows (shared by its sections).</summary>
internal static class ScreenCapturePage
{
    public static CaptureTool Selected { get; private set; } = CaptureTool.Screenshot;

    public static event Action? Changed;

    public static void Select(CaptureTool tool)
    {
        if (Selected == tool)
        {
            return;
        }

        Selected = tool;
        Changed?.Invoke();
    }

    public static void FromAnchor(string? anchor, IFeatureRuntime runtime)
    {
        foreach (var t in Enum.GetValues<CaptureTool>())
        {
            if (CaptureTools.Anchor(t) == anchor && runtime.IsAvailable(CaptureTools.FeatureId(t)))
            {
                Select(t);
                return;
            }
        }
    }

    public static void EnsureAvailable(IFeatureRuntime runtime)
    {
        if (!runtime.IsAvailable(CaptureTools.FeatureId(Selected)))
        {
            foreach (var t in Enum.GetValues<CaptureTool>())
            {
                if (runtime.IsAvailable(CaptureTools.FeatureId(t)))
                {
                    Select(t);
                    return;
                }
            }
        }
    }
}
