// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Settings editors for lists of audio devices (shared by output switcher and audio priority).</summary>
internal static class DeviceEditors
{
    /// <summary>A card with one check box per audio device; the checked ones are stored under <paramref name="key"/>.</summary>
    public static FrameworkElement Checklist(IFeatureSettings settings, string key, DataFlow flow, string title, string description)
    {
        var host = new StackPanel();
        void Render()
        {
            host.Children.Clear();
            var saved = settings.Get(key, new List<DevicePref>());
            var active = AudioDevices.List(flow);
            foreach (var device in active)
            {
                var check = new CheckBox { Content = device.Name, IsChecked = saved.Any(s => s.Id == device.Id), Margin = new Thickness(0, 3, 0, 3) };
                var captured = device;
                check.Click += (_, _) =>
                {
                    var list = settings.Get(key, new List<DevicePref>()).Where(s => s.Id != captured.Id).ToList();
                    if (check.IsChecked == true)
                    {
                        list.Add(new DevicePref { Id = captured.Id, Name = captured.Name });
                    }

                    settings.Set(key, list);
                };
                host.Children.Add(check);
            }

            if (active.Count == 0)
            {
                host.Children.Add(Muted("No active audio devices found."));
            }
        }

        Render();
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        refresh.Click += (_, _) => Render();
        return Card(title, description, host, refresh);
    }

    /// <summary>A card editing an ordered list of preferred devices (first = highest priority).</summary>
    public static FrameworkElement PriorityList(IFeatureSettings settings, string key, DataFlow flow, string title, string description)
    {
        var host = new StackPanel();
        void Render()
        {
            host.Children.Clear();
            var list = settings.Get(key, new List<DevicePref>());
            var activeIds = AudioDevices.List(flow);
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                var pref = list[i];
                var connected = activeIds.Any(a => a.Id == pref.Id);
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new TextBlock { Text = $"{i + 1}.  {pref.Name}{(connected ? string.Empty : "  (not connected)")}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                label.SetResourceReference(TextBlock.ForegroundProperty, connected ? ThemeKeys.Text : ThemeKeys.TextMuted);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(Small("▲", "Move up", () => Move(index, -1)));
                buttons.Children.Add(Small("▼", "Move down", () => Move(index, 1)));
                buttons.Children.Add(Small("✕", "Remove", () =>
                {
                    var l = settings.Get(key, new List<DevicePref>());
                    if (index < l.Count)
                    {
                        l.RemoveAt(index);
                        settings.Set(key, l);
                    }

                    Render();
                }));
                Grid.SetColumn(buttons, 1);
                row.Children.Add(label);
                row.Children.Add(buttons);
                host.Children.Add(row);
            }

            if (list.Count == 0)
            {
                host.Children.Add(Muted("No devices yet - add the ones you want, in order of preference."));
            }

            var candidates = AudioDevices.List(flow).Where(a => list.All(l => l.Id != a.Id)).ToList();
            var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var combo = new ComboBox { ItemsSource = candidates, DisplayMemberPath = nameof(AudioEndpoint.Name), MinWidth = 240, SelectedIndex = candidates.Count > 0 ? 0 : -1 };
            var addButton = new Button { Content = "Add", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = candidates.Count > 0 };
            addButton.Click += (_, _) =>
            {
                if (combo.SelectedItem is AudioEndpoint picked)
                {
                    var l = settings.Get(key, new List<DevicePref>());
                    l.Add(new DevicePref { Id = picked.Id, Name = picked.Name });
                    settings.Set(key, l);
                    Render();
                }
            };
            add.Children.Add(combo);
            add.Children.Add(addButton);
            host.Children.Add(add);
        }

        void Move(int index, int delta)
        {
            var l = settings.Get(key, new List<DevicePref>());
            var target = index + delta;
            if (index < 0 || index >= l.Count || target < 0 || target >= l.Count)
            {
                return;
            }

            (l[index], l[target]) = (l[target], l[index]);
            settings.Set(key, l);
            Render();
        }

        Render();
        return Card(title, description, host);
    }

    private static Button Small(string glyph, string tip, Action click)
    {
        var button = new Button { Content = glyph, ToolTip = tip, Width = 28, Height = 26, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0), FontSize = 11 };
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        return block;
    }

    private static Border Card(string title, string description, params UIElement[] content)
    {
        var panel = new StackPanel();
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        panel.Children.Add(heading);
        var desc = Muted(description);
        desc.Margin = new Thickness(0, 2, 0, 8);
        panel.Children.Add(desc);
        foreach (var element in content)
        {
            panel.Children.Add(element);
        }

        var border = new Border { Child = panel };
        border.SetResourceReference(Border.StyleProperty, ThemeKeys.CardStyle);
        return border;
    }
}
