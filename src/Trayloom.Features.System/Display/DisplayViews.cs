// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>Views of the Displays feature: the DISPLAYS tab of the panel and the Energy page card.</summary>
internal static class DisplayViews
{
    // ------------------------------------------------------------------------------------------------ rows (shared)

    /// <summary>The list of display rows; rebuilt when the service reports a change.</summary>
    private static FrameworkElement Rows(IContributionServices c, bool settings)
    {
        var host = new StackPanel();
        var service = DisplayService.Instance;
        if (service is null)
        {
            host.Children.Add(UiKit.Text(Strings.Displays.NoDisplay, settings ? 11.5 : 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            return host;
        }

        var failures = new Dictionary<string, string>();
        var sliderGuard = false;
        var signature = string.Empty;
        var sliders = new Dictionary<string, (Slider Slider, TextBlock Percent)>();

        void Render()
        {
            var items = service.Items;
            var sig = string.Join('|', items.Select(i => $"{i.Key}:{i.IsActive}:{i.SoftwareChosen}:{i.Pending}:{failures.GetValueOrDefault(i.Key)}"));
            if (sig == signature && sliders.Count > 0)
            {
                // Same structure: only refresh the values.
                sliderGuard = true;
                foreach (var item in items)
                {
                    if (sliders.TryGetValue(item.Key, out var pair) && !pair.Slider.IsMouseCaptureWithin)
                    {
                        pair.Slider.Value = item.Level;
                        pair.Percent.Text = item.Level + "%";
                    }
                }

                sliderGuard = false;
                return;
            }

            signature = sig;
            host.Children.Clear();
            sliders.Clear();
            if (items.Count == 0)
            {
                host.Children.Add(UiKit.Text(service.Items.Count == 0 && !_detected ? Strings.Displays.Detecting : Strings.Displays.NoDisplay, settings ? 11.5 : 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                return;
            }

            foreach (var item in items)
            {
                var row = settings ? SettingsRow(item) : PanelRow(item);
                row.Margin = new Thickness(0, host.Children.Count == 0 ? 0 : settings ? 13 : 10, 0, 0);
                host.Children.Add(row);
            }
        }

        FrameworkElement PanelRow(DisplayItem item)
        {
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UiKit.Icon(item.IsBuiltIn ? Gl.Laptop : Gl.Display, 10.5, item.IsActive ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, FontWeights.SemiBold);
            head.Children.Add(icon);
            var name = UiKit.Text(item.Name, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.Margin = new Thickness(4, 0, 6, 0);
            Grid.SetColumn(name, 1);
            head.Children.Add(name);
            var percent = UiKit.Text(item.IsActive ? item.Level + "%" : Strings.Displays.Off, 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            percent.FontFamily = UiKit.DisplayFont;
            percent.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(percent, 2);
            head.Children.Add(percent);
            var power = PowerButton(item);
            Grid.SetColumn(power, 3);
            head.Children.Add(power);
            stack.Children.Add(head);

            if (item.IsActive)
            {
                var slider = MakeSlider(item, percent);
                slider.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(slider);
                if (item.CanChooseDimming)
                {
                    var dim = DimButton(item, 10);
                    dim.Margin = new Thickness(0, 4, 0, 0);
                    stack.Children.Add(dim);
                }
            }

            AddFailure(stack, item, 10.5);
            return stack;
        }

        FrameworkElement SettingsRow(DisplayItem item)
        {
            var stack = new StackPanel();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 150, MaxWidth = 220 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tint = item.IsActive ? ThemeKeys.Accent : ThemeKeys.TextSecondary;
            var tile = UiKit.IconTile(item.IsBuiltIn ? Gl.Laptop : Gl.Display, 26, 12, 7, tint, item.IsActive ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
            row.Children.Add(tile);
            var name = UiKit.Text(item.Name, 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 2);
            row.Children.Add(name);
            var percent = UiKit.Text(item.IsActive ? item.Level + "%" : Strings.Displays.Off, 13, FontWeights.Normal, ThemeKeys.TextSecondary);
            percent.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            percent.Width = 52;
            percent.TextAlignment = TextAlignment.Right;
            percent.VerticalAlignment = VerticalAlignment.Center;
            if (item.IsActive)
            {
                var slider = MakeSlider(item, percent);
                slider.Margin = new Thickness(14, 0, 8, 0);
                Grid.SetColumn(slider, 3);
                row.Children.Add(slider);
            }

            Grid.SetColumn(percent, 4);
            row.Children.Add(percent);
            var power = PowerButton(item);
            power.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(power, 6);
            row.Children.Add(power);
            stack.Children.Add(row);

            // On its own line under the name: in the row grid this button squeezed the slider.
            if (item.CanChooseDimming)
            {
                var dim = DimButton(item, 12);
                dim.Margin = new Thickness(global::Trayloom.Core.Ui.SettingsRow.TextInset, 4, 0, 0);
                dim.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(dim);
            }

            AddFailure(stack, item, 11.5);
            return stack;
        }

        Slider MakeSlider(DisplayItem item, TextBlock percent)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = item.Level, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
            sliders[item.Key] = (slider, percent);
            slider.ValueChanged += (_, e) =>
            {
                percent.Text = (int)Math.Round(e.NewValue) + "%";
                if (!sliderGuard)
                {
                    service.SetLevel(item, (int)Math.Round(e.NewValue));
                }
            };
            return slider;
        }

        Button DimButton(DisplayItem item, double size)
        {
            var chosen = item.SoftwareChosen;
            var readable = item.Device is not null;
            var label = readable && !chosen ? Strings.Displays.ExtraDimming : Strings.Displays.DimPicture;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(UiKit.Icon(chosen ? Gl.CheckCircle : Gl.Dim, size + 1, chosen ? ThemeKeys.Accent : ThemeKeys.TextSecondary));
            var text = UiKit.Text(label, size, FontWeights.Medium, chosen ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
            text.Margin = new Thickness(4, 0, 0, 0);
            row.Children.Add(text);
            var b = new Button { Content = row, Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };
            b.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            b.IsEnabled = !item.Pending && (readable || !chosen);
            b.Click += (_, _) => service.SetSoftware(item, !item.SoftwareChosen);
            return b;
        }

        FrameworkElement PowerButton(DisplayItem item)
        {
            var active = item.IsActive;
            var lastActive = active && service.Items.Count(i => i.IsActive) <= 1;
            var button = Bits.FlatIcon(Gl.Power, active ? Strings.Displays.TurnOff : Strings.Displays.TurnOn, () => { }, 10.5, 22, active ? ThemeKeys.TextSecondary : ThemeKeys.Green);
            if (item.Pending)
            {
                button.IsEnabled = false;
                button.ToolTip = null;
            }

            if (lastActive)
            {
                button.IsEnabled = false;
                button.ToolTip = Strings.Displays.LastDisplay;
            }

            button.Click += async (_, _) =>
            {
                var error = await service.SetPowerAsync(item, !item.IsActive);
                if (error is null)
                {
                    failures.Remove(item.Key);
                }
                else
                {
                    failures[item.Key] = error;
                }

                signature = string.Empty;
                Render();
            };
            return button;
        }

        void AddFailure(StackPanel stack, DisplayItem item, double size)
        {
            var text = failures.GetValueOrDefault(item.Key) ?? item.Failure;
            if (!string.IsNullOrEmpty(text))
            {
                var line = UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.Red, wrap: true);
                line.Margin = new Thickness(settings ? SettingsRow_Inset : 0, 4, 0, 0);
                stack.Children.Add(line);
            }
        }

        void OnChanged() => host.Dispatcher.BeginInvoke(Render);
        host.Loaded += async (_, _) =>
        {
            service.Changed += OnChanged;
            if (service.Items.Count == 0)
            {
                Render();
                await service.ItemsAsync(rescan: true);
                _detected = true;
            }

            _detected = true;
            signature = string.Empty;
            Render();
        };
        host.Unloaded += (_, _) => service.Changed -= OnChanged;
        Render();
        return host;
    }

    private const double SettingsRow_Inset = 38;
    private static bool _detected;

    // ------------------------------------------------------------------------------------------------ panel tab

    public static FrameworkElement PanelCard(IContributionServices c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(Rows(c, settings: false));

        var osd = Bits.ToggleLine(Strings.Displays.ShowOsd, Strings.Displays.ShowOsdCaption, app.Get<bool>("brightnessOSDEnabled"), on => app.Set("brightnessOSDEnabled", on), SwitchSize.Mini, 11, captionBelow: false);
        stack.AddSpaced(UiKit.Divider(), 10);
        stack.AddSpaced(osd, 10);
        stack.AddSpaced(UiKit.Divider(), 10);
        stack.AddSpaced(Bits.Disclosure(Strings.KeepAwake.Options, () => OptionsBody(c, compact: true), expanded: false, indent: 19), 10);
        return new PanelCard(stack);
    }

    private static FrameworkElement OptionsBody(IContributionServices c, bool compact, UIElement? beforeCaption = null)
    {
        var app = c.App;
        var stack = new StackPanel();
        var shortcuts = new StackPanel();
        void Shortcuts()
        {
            shortcuts.Children.Clear();
            if (!app.Get<bool>("displayBrightnessShortcutsEnabled"))
            {
                return;
            }

            var down = ShortcutRows.Create(c, "displayBrightnessDecrease", Strings.Displays.Decrease, compact ? 10.5 : 12, Strings.OutputSwitcher.Rejected);
            down.Margin = new Thickness(23, 6, 0, 0);
            shortcuts.Children.Add(down);
            var up = ShortcutRows.Create(c, "displayBrightnessIncrease", Strings.Displays.Increase, compact ? 10.5 : 12, Strings.OutputSwitcher.Rejected);
            up.Margin = new Thickness(23, 6, 0, 0);
            shortcuts.Children.Add(up);
        }

        stack.Children.Add(Bits.CheckLine(Strings.Displays.UseShortcuts, Strings.Displays.UseShortcutsCaption, app.Get<bool>("displayBrightnessShortcutsEnabled"), on =>
        {
            app.Set("displayBrightnessShortcutsEnabled", on);
            Shortcuts();
        }));
        stack.Children.Add(shortcuts);
        Shortcuts();

        var steps = new[] { "standard", "half", "quarter" };
        var labels = new[] { Strings.Displays.StepStandard, Strings.Displays.StepHalf, Strings.Displays.StepQuarter };
        var stepRow = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        stepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(Strings.Displays.KeySteps, 11, FontWeights.Medium, ThemeKeys.TextPrimary));
        var caption = UiKit.Text(Strings.Displays.KeyStepsCaption, 9.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(caption);
        stepRow.Children.Add(text);
        var index = Math.Max(0, Array.IndexOf(steps, app.Get<string>("brightnessKeyStep")));
        var combo = Bits.MiniCombo(labels, index, i => app.Set("brightnessKeyStep", steps[i]), 112, 10.5);
        combo.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(combo, 1);
        stepRow.Children.Add(combo);
        stack.Children.Add(stepRow);

        if (beforeCaption is not null)
        {
            stack.Children.Add(beforeCaption);
        }

        var external = Bits.Caption(Strings.Displays.ExternalCaption, compact ? 9.5 : 11.5);
        external.Margin = new Thickness(0, 9, 0, 0);
        stack.Children.Add(external);
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ settings card

    public static FrameworkElement SettingsCard(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new SettingsCard();
        var detail = new StackPanel();

        void RenderDetail()
        {
            detail.Children.Clear();
            if (!app.Get<bool>("brightnessControlEnabled"))
            {
                detail.Visibility = Visibility.Collapsed;
                return;
            }

            detail.Visibility = Visibility.Visible;
            var divider = UiKit.Divider();
            divider.Margin = new Thickness(0, 0, 0, 13);
            detail.Children.Add(divider);
            detail.Children.Add(Rows(c, settings: true));
            var more = Bits.Disclosure(Strings.Displays.MoreOptions, () => MoreOptions(c), expanded: false, indent: 0, fontSize: 12);
            more.Margin = new Thickness(0, 13, 0, 0);
            detail.Children.Add(more);
        }

        var enable = new MacSwitch(app.Get<bool>("brightnessControlEnabled"), SwitchSize.Regular, on =>
        {
            app.Set("brightnessControlEnabled", on);
            RenderDetail();
        });
        card.Add(new SettingsRow(Gl.Display, Strings.Displays.ControlDisplays, Strings.Displays.ControlDisplaysCaption, enable));
        card.Add(detail);
        RenderDetail();
        return card;
    }

    private static FrameworkElement MoreOptions(ISettingsContext c)
    {
        var app = c.App;
        var osd = new SettingsRow(Gl.Sun, Strings.Displays.ShowOsd, Strings.Displays.ShowOsdCaption, new MacSwitch(app.Get<bool>("brightnessOSDEnabled"), SwitchSize.Regular, on => app.Set("brightnessOSDEnabled", on)));
        osd.Margin = new Thickness(0, 13, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(OptionsBody(c, compact: false, beforeCaption: osd));
        return stack;
    }
}
