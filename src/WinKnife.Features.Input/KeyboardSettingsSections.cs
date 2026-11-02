// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

// ------------------------------------------------------------------------------------------------------ debounce

/// <summary>The "Debounce" page: global window and per-key overrides.</summary>
public sealed class KeyDebounceSection : ISettingsSection
{
    public string PageId => SettingsPages.Debounce;

    public string? FeatureId => "keyboard-debounce";

    public int Order => 0;

    public string? Title => Strings.DebounceTitle;

    public IReadOnlyList<string> Keywords => ["Filter duplicate keys", "Global window", "Specific keys", "chatter", "double key press"];

    /// <summary>The key catalogue (Windows virtual-key codes) in display order.</summary>
    internal static IReadOnlyList<(int Vk, string Label)> Catalogue { get; } = BuildCatalogue();

    private static List<(int, string)> BuildCatalogue()
    {
        var list = new List<(int, string)>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            list.Add((c, c.ToString()));
        }

        for (var c = '0'; c <= '9'; c++)
        {
            list.Add((c, c.ToString()));
        }

        list.AddRange([
            (0x20, "Space"), (0x0D, "Enter"), (0x09, "Tab"), (0x08, "Backspace"), (0x1B, "Escape"),
            (0xBC, ","), (0xBE, "."), (0xBF, "/"), (0xBA, ";"), (0xDE, "'"), (0xBD, "-"), (0xBB, "="), (0xDB, "["), (0xDD, "]"), (0xDC, "\\"), (0xC0, "`"),
        ]);
        return list;
    }

    internal static string KeyLabel(int vk) => Catalogue.FirstOrDefault(k => k.Vk == vk).Label ?? $"#{vk}";

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var live = new LiveView(c);
        var stack = new StackPanel();

        // Section "Debounce"
        var main = new SettingsCard();
        var window = app.Get<int>(K.KeyDebounceWindow);
        var step = new StackPanel();
        main.Add(Rows.Toggle(app, "", Strings.FilterDuplicateKeys, Strings.FilterDuplicateKeysCaption, K.KeyDebounceEnabled));
        var active = InputUi.ActiveBadge(Strings.FilterActive, Rows.Inset);
        main.Add(live.Show(active, () => app.Get<bool>(K.KeyDebounceEnabled) && c.Runtime.IsRunning("keyboard-debounce")));
        var globalRow = InputUi.LabelLine(Strings.GlobalWindow, InputUi.Stepper(0, 500, window is >= 0 and <= 500 ? window : 5, v => $"{v} ms", v => app.Set(K.KeyDebounceWindow, v)));
        globalRow.Margin = new Thickness(Rows.Inset, 0, 0, 0);
        main.Add(globalRow);
        _ = step;
        stack.Children.Add(InputUi.FormSection(Strings.DebounceTitle, main));

        // Section "Specific keys"
        var specific = new SettingsCard();
        specific.Add(InputUi.Caption(Strings.SpecificKeysCaption));
        var list = new StackPanel();
        int? selectedVk = null;
        var addWindow = 5;
        var picker = new ComboBox { MinWidth = 160, ItemsSource = new[] { Strings.KeyPlaceholder }.Concat(Catalogue.Select(k => k.Label)).ToList(), SelectedIndex = 0 };
        picker.SelectionChanged += (_, _) => selectedVk = picker.SelectedIndex > 0 ? Catalogue[picker.SelectedIndex - 1].Vk : null;
        var addStepper = InputUi.Stepper(0, 500, addWindow, v => $"{v} ms", v => addWindow = v);
        var addButton = UiKit.Button(Strings.AddKey, ButtonKind.Bordered, 12, null, () =>
        {
            if (selectedVk is { } vk)
            {
                var map = KeyDebounceFeature.ParseOverrides(app.Get<string>(K.KeyDebounceKeys));
                map[vk] = addWindow;
                app.Set(K.KeyDebounceKeys, KeyDebounceFeature.FormatOverrides(map));
                Render();
            }
        });
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        addRow.Children.Add(picker);
        addStepper.HorizontalAlignment = HorizontalAlignment.Left;
        addStepper.Margin = new Thickness(12, 0, 0, 0);
        addRow.Children.Add(addStepper);
        addButton.Margin = new Thickness(12, 0, 0, 0);
        addRow.Children.Add(addButton);
        specific.Add(addRow);
        specific.Add(list);

        void Render()
        {
            list.Children.Clear();
            var map = KeyDebounceFeature.ParseOverrides(app.Get<string>(K.KeyDebounceKeys));
            if (map.Count == 0)
            {
                list.Children.Add(InputUi.Caption(Strings.NoSpecificKeys, 0, ThemeKeys.TextTertiary));
                return;
            }

            foreach (var (vk, ms) in map.OrderBy(p => KeyLabel(p.Key), StringComparer.Ordinal))
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = UiKit.Text(KeyLabel(vk), 12, FontWeights.SemiBold, ThemeKeys.TextPrimary);
                label.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(label);
                var key = vk;
                var stepper = InputUi.Stepper(0, 500, ms, v => $"{v} ms", v =>
                {
                    var current = KeyDebounceFeature.ParseOverrides(app.Get<string>(K.KeyDebounceKeys));
                    current[key] = v;
                    app.Set(K.KeyDebounceKeys, KeyDebounceFeature.FormatOverrides(current));
                });
                stepper.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(stepper, 1);
                grid.Children.Add(stepper);
                var trash = new Button { Content = UiKit.Icon("", 13, ThemeKeys.TextSecondary), ToolTip = Strings.RemoveKey, Padding = new Thickness(4) };
                trash.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
                trash.Click += (_, _) =>
                {
                    var current = KeyDebounceFeature.ParseOverrides(app.Get<string>(K.KeyDebounceKeys));
                    current.Remove(key);
                    app.Set(K.KeyDebounceKeys, KeyDebounceFeature.FormatOverrides(current));
                    Render();
                };
                Grid.SetColumn(trash, 2);
                grid.Children.Add(trash);
                list.Children.Add(grid);
            }
        }

        Render();
        var section = InputUi.FormSection(Strings.SpecificKeys, specific);
        void Enable() => section.IsEnabled = app.Get<bool>(K.KeyDebounceEnabled);
        Enable();
        live.OnChange(Enable);
        section.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(section);
        return live.Attach(stack);
    }
}

// ------------------------------------------------------------------------------------------------------ super key

/// <summary>The "Super key" page: source key, modifier diagram, solo tap action and the paused-apps list.</summary>
public sealed class SuperKeySection : ISettingsSection
{
    private static readonly (string Value, string Label)[] Sources =
    [
        ("capsLock", Strings.CapsLock), ("rightCommand", Strings.RightWin), ("rightOption", Strings.RightAlt), ("rightControl", Strings.RightCtrl), ("rightShift", Strings.RightShift),
    ];

    public string PageId => SettingsPages.SuperKey;

    public string? FeatureId => "super-key";

    public int Order => 0;

    public string? Title => Strings.SuperKeyTitle;

    public IReadOnlyList<string> Keywords => ["Key to hold", "Caps Lock", "A tap on its own", "modifier", "hyper key"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var live = new LiveView(c);
        var stack = new StackPanel();

        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, null, Strings.UseSuperKey, null, K.SuperEnabled));
        card.AddDivider();
        card.Add(new PickerRow<string>(null, Strings.KeyToHold, null, Sources.Select(s => (s.Value, s.Label)).ToList(), app.Get<string>(K.SuperSource), v => app.Set(K.SuperSource, v), 170));
        card.Add(InputUi.Caption(Strings.SuperKeyHint));
        var note = new StackPanel { Orientation = Orientation.Horizontal };
        var info = UiKit.Icon("", 12, ThemeKeys.TextSecondary);
        info.Margin = new Thickness(0, 1, 6, 0);
        note.Children.Add(info);
        note.Children.Add(UiKit.Text(Strings.SuperKeyNormalFunction, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        card.Add(note);
        card.AddDivider();

        var diagram = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        void RenderDiagram()
        {
            diagram.Children.Clear();
            var enabled = app.Get<bool>(K.SuperEnabled);
            var source = app.Get<string>(K.SuperSource) ?? "capsLock";
            var sourceLabel = Sources.FirstOrDefault(s => s.Value == source).Label ?? Strings.CapsLock;
            var selected = SuperKeyFeature.ParseModifiers(app.Get<string>(K.SuperModifiers));

            var left = new StackPanel();
            var cap = InputUi.KeyCap(sourceLabel, false, 84, 30, 12, tinted: false, bold: true);
            cap.HorizontalAlignment = HorizontalAlignment.Center;
            left.Children.Add(cap);
            var hold = UiKit.Text(Strings.Hold, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
            hold.HorizontalAlignment = HorizontalAlignment.Center;
            hold.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(hold);
            diagram.Children.Add(left);

            var arrow = UiKit.Icon("", 11, ThemeKeys.TextTertiary, FontWeights.SemiBold);
            arrow.Margin = new Thickness(10, 0, 10, 14);
            diagram.Children.Add(arrow);

            var caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            foreach (var (token, vk, label) in new[] { ("shift", (ushort)0xA0, Strings.Shift), ("control", (ushort)0xA2, Strings.Ctrl), ("option", (ushort)0xA4, Strings.Alt), ("command", (ushort)0x5B, Strings.Win) })
            {
                var on = selected.Contains(vk);
                var t = token;
                var keycap = InputUi.KeyCap(label, on, 30, 30, 12, tinted: true, tooltip: label, onClick: () =>
                {
                    if (!app.Get<bool>(K.SuperEnabled))
                    {
                        return;
                    }

                    var tokens = new List<string> { "control", "option", "shift", "command" }.Where(x => x == t ? !on : Has(selected, x)).ToList();
                    // At least one of Control / Alt / Win must stay (Shift alone is not a super key).
                    if (!tokens.Any(x => x is "control" or "option" or "command"))
                    {
                        return;
                    }

                    app.Set(K.SuperModifiers, string.Join("+", tokens));
                    RenderDiagram();
                });
                keycap.Margin = new Thickness(0, 0, 5, 0);
                caps.Children.Add(keycap);
            }

            var rightCaps = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            rightCaps.Children.Add(caps);
            diagram.Children.Add(rightCaps);
            diagram.Opacity = enabled ? 1 : 0.5;
        }

        RenderDiagram();
        live.OnChange(RenderDiagram);
        card.Add(diagram);
        card.AddDivider();

        var status = new StackPanel { Orientation = Orientation.Horizontal };
        void RenderStatus()
        {
            status.Children.Clear();
            var enabled = app.Get<bool>(K.SuperEnabled);
            if (!enabled)
            {
                status.Visibility = Visibility.Collapsed;
                return;
            }

            status.Visibility = Visibility.Visible;
            var (glyph, key, text) = SuperKeyStatus.Current switch
            {
                SuperKeyState.Paused => ("", ThemeKeys.TextSecondary, Strings.PausedWhileAppOpen),
                SuperKeyState.HookFailed => ("", ThemeKeys.Orange, Strings.KeyboardTapRefused),
                SuperKeyState.Working => ("", ThemeKeys.Green, Strings.WorkingNow),
                _ => (string.Empty, ThemeKeys.TextSecondary, string.Empty),
            };
            if (text.Length == 0)
            {
                status.Visibility = Visibility.Collapsed;
                return;
            }

            var icon = UiKit.Icon(glyph, 14, key);
            icon.Margin = new Thickness(0, 0, 8, 0);
            status.Children.Add(icon);
            status.Children.Add(UiKit.Text(text, 12.5, FontWeights.Normal, key, wrap: true));
        }

        RenderStatus();
        live.OnChange(RenderStatus);
        card.Add(status);
        void StatusChanged() => c.Ui.BeginInvoke(RenderStatus);
        stack.Loaded += (_, _) => SuperKeyStatus.Changed += StatusChanged;
        stack.Unloaded += (_, _) => SuperKeyStatus.Changed -= StatusChanged;
        stack.Children.Add(InputUi.FormSection(Strings.SuperKeyTitle, card));

        // Section "A tap on its own"
        var solo = new SettingsCard();
        var group = "solo" + Guid.NewGuid().ToString("N");
        var radios = new StackPanel();
        foreach (var (value, label) in new[] { ("none", Strings.SoloNothing), ("capsLock", Strings.SoloCapitals), ("inputSource", Strings.SoloInputSource), ("escape", Strings.SoloEscape) })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var radioLabel = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
            radioLabel.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(radioLabel);
            var radio = new RadioButton { GroupName = group, IsChecked = (app.Get<string>(K.SuperSolo) ?? "none") == value, VerticalAlignment = VerticalAlignment.Center };
            var v = value;
            radio.Checked += (_, _) => app.Set(K.SuperSolo, v);
            Grid.SetColumn(radio, 1);
            row.Children.Add(radio);
            if (radios.Children.Count > 0)
            {
                radios.Children.Add(UiKit.Divider());
                ((FrameworkElement)radios.Children[^1]).Margin = new Thickness(0, 10, 0, 10);
            }

            radios.Children.Add(row);
        }

        solo.Add(radios);
        solo.AddDivider();
        solo.AddCaption(Strings.SoloCaption);
        var soloSection = InputUi.FormSection(Strings.SoloSection, solo);
        void EnableSolo() => soloSection.IsEnabled = app.Get<bool>(K.SuperEnabled);
        EnableSolo();
        live.OnChange(EnableSolo);
        soloSection.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(soloSection);

        var exceptionsCard = new SettingsCard();
        exceptionsCard.Add(new AppBundleList(app, K.SuperExceptions, Strings.CaptionSuperKey));
        var exceptions = live.Show(exceptionsCard, () => app.Get<bool>(K.SuperEnabled));
        exceptions.Margin = new Thickness(0, 20, 0, 0);
        stack.Children.Add(exceptions);
        return live.Attach(stack);
    }

    private static bool Has(ushort[] selected, string token) => token switch
    {
        "control" => selected.Contains((ushort)0xA2),
        "option" => selected.Contains((ushort)0xA4),
        "shift" => selected.Contains((ushort)0xA0),
        _ => selected.Contains((ushort)0x5B),
    };
}

// ------------------------------------------------------------------------------------------------------ quit protection

/// <summary>The "Quit &amp; close protection" page: an intro and two independent sections (Alt+F4 and Ctrl+W).</summary>
public sealed class QuitProtectionSection : ISettingsSection
{
    public string PageId => SettingsPages.QuitProtection;

    public string? FeatureId => "quit-window-protection";

    public int Order => 0;

    public string? Title => Strings.QuitProtectionTitle;

    public IReadOnlyList<string> Keywords => ["Alt+F4", "Ctrl+W", "Hold to confirm", "Double press", "Require extra modifier", "Protect this shortcut"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var stack = new StackPanel();

        var intro = new SettingsCard();
        intro.Add(InputUi.Caption(Strings.QuitIntro));
        var hand = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon("", 12, ThemeKeys.TextSecondary);
        icon.Margin = new Thickness(0, 1, 6, 0);
        hand.Children.Add(icon);
        hand.Children.Add(UiKit.Text(Strings.QuitObserve, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        intro.Add(hand);
        stack.Children.Add(intro);

        stack.Children.Add(Spaced(Shortcut(c, ProtectedShortcut.Quit)));
        stack.Children.Add(Spaced(Shortcut(c, ProtectedShortcut.Close)));
        return stack;
    }

    private static FrameworkElement Spaced(FrameworkElement element)
    {
        element.Margin = new Thickness(0, 20, 0, 0);
        return element;
    }

    private static FrameworkElement Shortcut(ISettingsContext c, ProtectedShortcut kind)
    {
        var app = c.App;
        string Key(string suffix) => kind == ProtectedShortcut.Quit ? K.Quit(suffix) : K.Close(suffix);
        var label = kind == ProtectedShortcut.Quit ? "Alt+F4" : "Ctrl+W";
        var live = new LiveView(c);
        var card = new SettingsCard();

        card.Add(Rows.Toggle(app, null, Strings.ProtectThisShortcut, Strings.OtherShortcutsWork, Key("Enabled")));
        var body = new StackPanel();
        void Enable() => body.IsEnabled = app.Get<bool>(Key("Enabled"));

        // Confirmation mode
        var modes = new (string Value, string Label)[] { ("hold", Strings.ModeHold), ("doublePress", Strings.ModeDouble), ("extraModifier", Strings.ModeExtra) };
        body.Children.Add(new PickerRow<string>(null, Strings.ConfirmationMode, null, modes, app.Get<string>(Key("Mode")), v => app.Set(Key("Mode"), v), 220));

        var hold = live.Show(HoldSlider(app, Key("HoldDurationMs"), Strings.HoldDuration, 250, 2000, "250 ms", "2 s"), () => app.Get<string>(Key("Mode")) == "hold");
        hold.Margin = new Thickness(0, 13, 0, 0);
        body.Children.Add(hold);
        var dbl = live.Show(HoldSlider(app, Key("DoubleIntervalMs"), Strings.DoubleInterval, 200, 1500, "200 ms", "1.5 s"), () => app.Get<string>(Key("Mode")) == "doublePress");
        dbl.Margin = new Thickness(0, 13, 0, 0);
        body.Children.Add(dbl);

        var extraOptions = kind == ProtectedShortcut.Quit
            ? new (string, string)[] { ("shift", Strings.Shift), ("control", Strings.Ctrl), ("command", Strings.Win) }
            : [("shift", Strings.Shift), ("option", Strings.Alt), ("command", Strings.Win)];
        var current = app.Get<string>(Key("ExtraModifier"));
        if (!extraOptions.Any(o => o.Item1 == current))
        {
            current = "shift";
        }

        var extraPreview = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        void PreviewText() => extraPreview.Text = CloseProtectionFeature.ExtraText(kind, ExtraKey(app.Get<string>(Key("ExtraModifier"))));
        var extra = new StackPanel();
        extra.Children.Add(new PickerRow<string>(null, Strings.ExtraModifier, null, extraOptions, current, v =>
        {
            app.Set(Key("ExtraModifier"), v);
            PreviewText();
        }, 160));
        extraPreview.Margin = new Thickness(0, 6, 0, 0);
        extra.Children.Add(extraPreview);
        PreviewText();
        live.Show(extra, () => app.Get<string>(Key("Mode")) == "extraModifier");
        extra.Margin = new Thickness(0, 13, 0, 0);
        body.Children.Add(extra);

        var scopes = new (string Value, string Label)[] { ("all", Strings.ScopeAll), ("selectedOnly", Strings.ScopeSelected), ("allExceptSelected", Strings.ScopeExcept) };
        var scope = new PickerRow<string>(null, Strings.Applications, null, scopes, app.Get<string>(Key("Scope")), v => app.Set(Key("Scope"), v), 240);
        scope.Margin = new Thickness(0, 13, 0, 0);
        body.Children.Add(scope);

        var apps = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };
        apps.Children.Add(UiKit.Text(Strings.SelectedApplications, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var list = new AppBundleList(app, Key("Exceptions"), string.Empty, Strings.SelectedApplications, Strings.AddApplication, compact: true, emptyText: Strings.NoApplicationsSelected);
        list.Margin = new Thickness(0, 8, 0, 0);
        apps.Children.Add(list);
        live.Show(apps, () => app.Get<string>(Key("Scope")) != "all");
        body.Children.Add(apps);

        var feedback = new SettingsRow(null, Strings.ShowVisualFeedback, null, new MacSwitch(app.Get<bool>(Key("ShowFeedback")), SwitchSize.Regular, on => app.Set(Key("ShowFeedback"), on)));
        feedback.Margin = new Thickness(0, 13, 0, 0);
        body.Children.Add(feedback);

        Enable();
        live.OnChange(Enable);
        card.Add(body);
        return live.Attach(InputUi.FormSection(label, card));
    }

    private static System.Windows.Input.ModifierKeys ExtraKey(string? value) => value switch
    {
        "option" => System.Windows.Input.ModifierKeys.Alt,
        "control" => System.Windows.Input.ModifierKeys.Control,
        "command" => System.Windows.Input.ModifierKeys.Windows,
        _ => System.Windows.Input.ModifierKeys.Shift,
    };

    private static FrameworkElement HoldSlider(IAppSettings app, string key, string label, double min, double max, string minLabel, string maxLabel)
    {
        var stack = new StackPanel();
        var value = Math.Clamp(app.Get<double>(key), min, max);
        var readout = UiKit.Text($"{(int)value} ms", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 50, IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{(int)e.NewValue} ms";
            app.Set(key, Math.Round(e.NewValue));
        };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        Grid.SetColumn(readout, 1);
        head.Children.Add(readout);
        stack.Children.Add(head);
        stack.Children.Add(slider);
        var ends = new Grid();
        ends.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ends.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ends.Children.Add(UiKit.Text(minLabel, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary));
        var max2 = UiKit.Text(maxLabel, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        Grid.SetColumn(max2, 1);
        ends.Children.Add(max2);
        stack.Children.Add(ends);
        return stack;
    }
}
