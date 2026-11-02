// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Shared row: label + shortcut recorder + reset + problem text (spec "ShortcutPreferenceRow").</summary>
internal static class ShortcutRows
{
    public static FrameworkElement Create(IContributionServices c, string roleId, string label, double labelSize = 11, string? rejectedText = null)
    {
        var stack = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(label, labelSize, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        var recorder = new HotkeyRecorder { Hotkey = c.Shortcuts.GetHotkey(roleId), Width = 128, MinWidth = 108 };
        var problem = UiKit.Text(string.Empty, 10.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        problem.Margin = new Thickness(0, 4, 0, 0);
        problem.Visibility = Visibility.Collapsed;
        Button? reset = null;
        reset = Bits.FlatIcon(Gl.Reset, "Reset", () =>
        {
            c.Shortcuts.Reset(roleId);
            recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            Update();
        }, 10, 22);

        void Update(string? error = null)
        {
            var state = c.Shortcuts.GetState(roleId);
            var message = error ?? state.Problem;
            problem.Text = message ?? string.Empty;
            problem.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            reset.Visibility = c.Shortcuts.IsDefault(roleId) ? Visibility.Collapsed : Visibility.Visible;
        }

        recorder.HotkeyChanged += (_, hotkey) =>
        {
            var result = c.Shortcuts.TrySet(roleId, hotkey);
            if (!result.Ok)
            {
                recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            }

            Update(result.Ok ? null : result.Error ?? rejectedText);
        };
        Grid.SetColumn(reset, 1);
        Grid.SetColumn(recorder, 2);
        row.Children.Add(reset);
        row.Children.Add(recorder);
        stack.Children.Add(row);
        stack.Children.Add(problem);
        Update();
        return stack;
    }
}

/// <summary>"Output switcher" controls (spec 02a 3.3): master check box, shortcut row and the "Outputs in cycle" list.</summary>
internal static class OutputSwitcherView
{
    public static FrameworkElement Build(IContributionServices c, bool compact)
    {
        var app = c.App;
        var size = compact ? 11.0 : 11.5;
        var stack = new StackPanel();
        var detail = new StackPanel();

        void Render()
        {
            detail.Children.Clear();
            if (!app.Get<bool>("soundOutputSwitcherEnabled"))
            {
                return;
            }

            var shortcut = ShortcutRows.Create(c, "soundOutputSwitcher", Strings.OutputSwitcher.Shortcut, 11, Strings.OutputSwitcher.Rejected);
            shortcut.Margin = new Thickness(0, 8, 0, 0);
            detail.Children.Add(shortcut);
            var heading = UiKit.Text(Strings.OutputSwitcher.OutputsInCycle, 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
            heading.Margin = new Thickness(0, 10, 0, 4);
            detail.Children.Add(heading);
            var devices = AudioDevices.List(DataFlow.Render);
            var defaultId = AudioDevices.DefaultId(DataFlow.Render);
            if (devices.Count == 0)
            {
                var none = new StackPanel { Orientation = Orientation.Horizontal };
                none.Children.Add(UiKit.Icon(Gl.SpeakerMute, 10, ThemeKeys.TextSecondary));
                var t = UiKit.Text(Strings.Mixer.NoOutputs, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
                t.Margin = new Thickness(6, 0, 0, 0);
                none.Children.Add(t);
                detail.Children.Add(none);
                return;
            }

            var selected = app.Get<List<string>>("soundOutputSwitcherDeviceUIDs");
            foreach (var device in devices)
            {
                var id = device.Id;
                var check = Bits.Check(selected.Contains(id));
                check.Margin = new Thickness(0, 1, 0, 1);
                var label = UiKit.Text(device.Name + (id == defaultId ? Strings.Mixer.CurrentSuffix : string.Empty), 10.5 / 0.8, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
                label.MaxWidth = 300;
                check.Content = label;
                check.Click += (_, _) =>
                {
                    var list = app.Get<List<string>>("soundOutputSwitcherDeviceUIDs").Where(x => x != id).ToList();
                    if (check.IsChecked == true)
                    {
                        list.Add(id);
                    }

                    // Selection order follows the system device order; unavailable-but-selected ids stay at the end.
                    var order = AudioDevices.List(DataFlow.Render).Select(d => d.Id).ToList();
                    app.Set("soundOutputSwitcherDeviceUIDs", list.OrderBy(x => order.IndexOf(x) is var i and >= 0 ? i : int.MaxValue).ToList());
                };
                detail.Children.Add(check);
            }
        }

        var master = Bits.CheckLine(Strings.OutputSwitcher.Switch, Strings.OutputSwitcher.Caption, app.Get<bool>("soundOutputSwitcherEnabled"), on =>
        {
            if (on && app.Get<List<string>>("soundOutputSwitcherDeviceUIDs").Count == 0 && AudioDevices.DefaultId(DataFlow.Render) is { } current)
            {
                app.Set("soundOutputSwitcherDeviceUIDs", new List<string> { current });
            }

            app.Set("soundOutputSwitcherEnabled", on);
            Render();
        }, size);
        stack.Children.Add(master);
        stack.Children.Add(detail);
        Render();
        return stack;
    }
}

/// <summary>"Audio priority" controls (spec 02a 3.4): caption, output and microphone priority lists with switches and drag handles.</summary>
internal static class AudioPriorityView
{
    public static FrameworkElement Build(IContributionServices c, bool compact = true)
    {
        var stack = new StackPanel();
        stack.Children.Add(Bits.Caption(Strings.AudioPriority.Caption, 9.5));
        stack.AddSpaced(Section(c, true, compact), 10);
        stack.AddSpaced(UiKit.Divider(), 10);
        stack.AddSpaced(Section(c, false, compact), 10);
        return stack;
    }

    private static FrameworkElement Section(IContributionServices c, bool output, bool compact)
    {
        var app = c.App;
        var flow = output ? DataFlow.Render : DataFlow.Capture;
        var uidsKey = output ? "audioPriorityOutputUIDs" : "audioPriorityInputUIDs";
        var enabledKey = output ? "audioPriorityOutputEnabled" : "audioPriorityInputEnabled";
        var section = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon(output ? Gl.Speaker : Gl.Mic, 10.5, ThemeKeys.TextSecondary);
        icon.Width = 16;
        title.Children.Add(icon);
        title.Children.Add(UiKit.Text(output ? Strings.AudioPriority.OutputPriority : Strings.AudioPriority.InputPriority, 11, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        head.Children.Add(title);
        var sw = new MacSwitch(app.Get<bool>(enabledKey), SwitchSize.Mini, on => app.Set(enabledKey, on)) { ToolTip = output ? Strings.AudioPriority.OutputSwitch : Strings.AudioPriority.InputSwitch };
        Grid.SetColumn(sw, 1);
        head.Children.Add(sw);
        section.Children.Add(head);

        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        section.Children.Add(list);

        void Render()
        {
            list.Children.Clear();
            var active = AudioDevices.List(flow);
            var stored = app.Get<List<string>>(uidsKey).ToList();
            var names = app.Get<Dictionary<string, string>>("audioPriorityDeviceNames");
            // Devices the service has not ranked yet are shown at the end until it does.
            foreach (var device in active.Where(a => !stored.Contains(a.Id)))
            {
                stored.Add(device.Id);
                names[device.Id] = device.Name;
            }

            if (stored.Count == 0)
            {
                list.Children.Add(UiKit.Text(output ? Strings.Mixer.NoOutputs : Strings.Mixer.NoMicrophones, 9.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                return;
            }

            var current = AudioDevices.DefaultId(flow);
            for (var i = 0; i < stored.Count; i++)
            {
                var uid = stored[i];
                var isActive = active.Any(a => a.Id == uid);
                var isCurrent = uid == current;
                var name = isActive ? active.First(a => a.Id == uid).Name : names.GetValueOrDefault(uid, uid);
                var row = new Border { Tag = uid, CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1) };
                row.SetResourceReference(Border.BackgroundProperty, isCurrent ? ThemeKeys.AccentFill12 : ThemeKeys.TileFill);
                row.SetResourceReference(Border.BorderBrushProperty, isCurrent ? ThemeKeys.Accent : ThemeKeys.TileFill);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var rank = UiKit.Text((i + 1).ToString(), 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
                rank.FontFamily = UiKit.DisplayFont;
                rank.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(rank);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
                var label = UiKit.Text(name, 10.5, FontWeights.Normal, isActive ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                label.MaxWidth = compact ? 120 : 380;
                nameLine.Children.Add(label);
                if (isCurrent)
                {
                    var cur = UiKit.Text(Strings.AudioPriority.Current, 9, FontWeights.Normal, ThemeKeys.Accent);
                    cur.Margin = new Thickness(6, 0, 0, 0);
                    cur.VerticalAlignment = VerticalAlignment.Center;
                    nameLine.Children.Add(cur);
                }

                text.Children.Add(nameLine);
                if (!isActive)
                {
                    text.Children.Add(UiKit.Text(Strings.AudioPriority.Unavailable, 9, FontWeights.Normal, ThemeKeys.Orange));
                }

                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                var handle = new Border { Width = 16, Height = 22, Background = Brushes.Transparent, Child = UiKit.Icon(Gl.Handle, 11, ThemeKeys.TextTertiary, FontWeights.SemiBold), ToolTip = Strings.AudioPriority.MoveUp + " / " + Strings.AudioPriority.MoveDown };
                Grid.SetColumn(handle, 2);
                grid.Children.Add(handle);
                row.Child = grid;
                list.Children.Add(row);
                Bits.AttachReorder(list, row, handle, rows =>
                {
                    var order = rows.Select(r => (string)((Border)r).Tag).ToList();
                    app.Set(uidsKey, order);
                    var nameMap = app.Get<Dictionary<string, string>>("audioPriorityDeviceNames");
                    foreach (var (id, n) in names)
                    {
                        nameMap[id] = n;
                    }

                    app.Set("audioPriorityDeviceNames", nameMap);
                    Render();
                });
            }
        }

        Render();
        // Refresh when the priority lists change (the service may append devices) so ranks stay current.
        void OnChanged(string key)
        {
            if (key == uidsKey || key == "audioPriorityDeviceNames")
            {
                section.Dispatcher.BeginInvoke(Render);
            }
        }

        section.Loaded += (_, _) => app.Changed += OnChanged;
        section.Unloaded += (_, _) => app.Changed -= OnChanged;
        return section;
    }
}
