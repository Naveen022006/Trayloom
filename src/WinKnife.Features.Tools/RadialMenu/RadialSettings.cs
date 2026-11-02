// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Settings &gt; Radial menu, card 1: master switch, opening behaviour, position and "Try it".</summary>
public sealed class RadialMenuMainSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "radial-menu";

    /// <inheritdoc />
    public string PageId => SettingsPages.RadialMenu;

    /// <inheritdoc />
    public string? Anchor => "radialMenu";

    /// <inheritdoc />
    public string? Title => "Radial menu";

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Opening behavior", "Press or hold", "Press to open", "Hold to select", "At the pointer", "At the screen center", "Try it", "wheel"];

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext context)
    {
        RadialState.Ctx ??= context;
        return new RadialLiveView(() => Build(context), c => c == RadialChange.Enabled);
    }

    private static UIElement Build(ISettingsContext c)
    {
        var enabled = c.App.Get<bool>("radialMenuEnabled");
        var service = c.Services.Get<IRadialMenuService>();
        var card = new SettingsCard("Radial menu");
        card.Add(new SettingsRow(null, "Use the radial menu", null, new MacSwitch(enabled, SwitchSize.Regular, on =>
        {
            c.App.Set("radialMenuEnabled", on);
            RadialState.Raise(RadialChange.Enabled);
        })));
        card.AddCaption(RadialStrings.HubDescription);
        if (enabled && service?.RegistrationFailed == true)
        {
            card.Add(UiKit.Text("This shortcut is already taken by another app.", 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
        }

        card.AddDivider();
        var mode = c.App.Get<string>("radialMenuActivationMode", "pressOrHold");
        var picker = new PickerRow<string>(null, "Opening behavior", null, [("pressOrHold", "Press or hold"), ("press", "Press to open"), ("hold", "Hold to select")], mode is "press" or "hold" ? mode : "pressOrHold", v => c.App.Set("radialMenuActivationMode", v));
        picker.IsEnabled = enabled;
        picker.Opacity = enabled ? 1 : 0.4;
        card.Add(picker);
        card.AddCaption("Press or hold keeps the current adaptive gesture. Press stays open; hold runs the highlighted action on release.");
        card.AddDivider();
        var segmented = new SegmentedControl(["At the pointer", "At the screen center"], c.App.Get<bool>("radialMenuAtPointer", true) ? 0 : 1);
        segmented.SelectionChanged += i => c.App.Set("radialMenuAtPointer", i == 0);
        card.Add(new SettingsRow(null, "Opens", null, segmented));
        card.AddDivider();
        var tryIt = UiKit.Button("Try it", ButtonKind.Bordered, 12.5, onClick: () =>
        {
            var profile = RadialState.Selected(c);
            c.Services.Get<IRadialMenuService>()?.TryIt(profile);
        });
        tryIt.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(tryIt);
        return card;
    }
}

/// <summary>Settings &gt; Radial menu, card 3: profiles, colour, shortcut, mouse button, four-finger tap.</summary>
public sealed class RadialMenuProfilesSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "radial-menu";

    /// <inheritdoc />
    public string PageId => SettingsPages.RadialMenu;

    /// <inheritdoc />
    public string? Anchor => "radialProfiles";

    /// <inheritdoc />
    public string? Title => "Profiles";

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Profile", "Add profile", "Duplicate profile", "Delete profile", "Color", "Shortcut", "Mouse button", "Back side button", "Forward side button", "four-finger tap", "Button test"];

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext context)
    {
        RadialState.Ctx ??= context;
        return new RadialLiveView(() => Build(context), c => c is RadialChange.Enabled or RadialChange.Profiles);
    }

    private static UIElement Build(ISettingsContext c)
    {
        var enabled = c.App.Get<bool>("radialMenuEnabled");
        var profiles = RadialState.Profiles(c);
        var selected = RadialState.Selected(c, profiles);
        var service = c.Services.Get<IRadialMenuService>();
        var card = new SettingsCard("Profiles");
        var body = new StackPanel { IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        card.Add(body);

        // ---- management row
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var combo = new ComboBox { ItemsSource = profiles.Select(p => p.DisplayName).ToList(), SelectedIndex = profiles.FindIndex(p => p.Id == selected.Id) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && profiles[combo.SelectedIndex].Id != RadialState.SelectedId)
            {
                RadialState.SelectedId = profiles[combo.SelectedIndex].Id;
                RadialState.Trail.Clear();
                RadialState.Raise(RadialChange.Profiles);
            }
        };
        var pickerRow = new SettingsRow(null, "Profile", null, combo);
        Grid.SetColumn(pickerRow, 0);
        row.Children.Add(pickerRow);
        var add = IconButton("", "Add profile");
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var preset in RadialModel.PresetIds)
            {
                var id = preset;
                var item = new MenuItem { Header = RadialModel.PresetTitle(id) };
                item.Click += (_, _) => RadialState.Mutate(c, (list, _) =>
                {
                    var profile = new RadialProfile
                    {
                        Name = id == "general" ? $"General {list.Count + 1}" : RadialModel.PresetTitle(id),
                        Color = RadialModel.PresetColor(id),
                        Preset = id,
                        Items = RadialModel.MakeItems(id),
                    };
                    list.Add(profile);
                    RadialState.SelectedId = profile.Id;
                    RadialState.Trail.Clear();
                }, RadialChange.Profiles);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        var duplicate = IconButton("", "Duplicate profile");
        duplicate.Click += (_, _) => RadialState.Mutate(c, (list, sel) =>
        {
            var copy = sel.Clone();
            copy.Id = Guid.NewGuid().ToString().ToUpperInvariant();
            copy.Name = sel.DisplayName + " 2";
            copy.Shortcut = "";
            copy.MouseButton = "off";
            copy.TrackpadTap = false;
            copy.Items = RadialClone(sel.Items);
            list.Insert(list.IndexOf(sel) + 1, copy);
            RadialState.SelectedId = copy.Id;
            RadialState.Trail.Clear();
        }, RadialChange.Profiles);
        var delete = IconButton("", "Delete profile");
        delete.IsEnabled = profiles.Count > 1;
        delete.Click += (_, _) =>
        {
            if (!RadialDialogs.Confirm(Window.GetWindow(delete), $"Delete “{selected.DisplayName}”?", "Its actions, shortcut, mouse button and four-finger tap will be removed. This can’t be undone.", "Delete profile", true))
            {
                return;
            }

            RadialState.Mutate(c, (list, sel) =>
            {
                var index = list.IndexOf(sel);
                list.Remove(sel);
                RadialState.SelectedId = list[Math.Min(index, list.Count - 1)].Id;
                RadialState.Trail.Clear();
            }, RadialChange.Profiles);
        };
        Grid.SetColumn(add, 1);
        Grid.SetColumn(duplicate, 2);
        Grid.SetColumn(delete, 3);
        row.Children.Add(add);
        row.Children.Add(duplicate);
        row.Children.Add(delete);
        body.Children.Add(row);

        if (enabled && !selected.HasTrigger)
        {
            body.AddSpaced(Caption("Picking a profile here only edits it. Give this one a shortcut, a mouse button or the four-finger tap below to open it."), 8);
        }

        // ---- name
        var nameBox = RadialDialogs.NewTextBox(selected.Name, 220);
        nameBox.TextChanged += (_, _) => RadialState.Mutate(c, (_, sel) => sel.Name = nameBox.Text, null);
        nameBox.LostKeyboardFocus += (_, _) => RadialState.Raise(RadialChange.Profiles);
        body.AddSpaced(new SettingsRow(null, "Profile name", null, RadialDialogs.WithPlaceholder(nameBox, "General")), 13);

        // ---- colour
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var id in RadialColors.Ids)
        {
            swatches.Children.Add(Swatch(c, id, selected.Color == id));
        }

        body.AddSpaced(new SettingsRow(null, "Color", null, swatches), 13);

        // ---- shortcut
        var recorder = new HotkeyRecorder { Width = 150, Hotkey = Hotkey.TryParse(selected.Shortcut, out var hk) ? hk : null };
        var error = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        error.Visibility = Visibility.Collapsed;
        recorder.HotkeyChanged += (_, h) =>
        {
            var message = ValidateShortcut(c, h, selected, profiles);
            if (message is not null)
            {
                error.Text = message;
                error.Visibility = Visibility.Visible;
                recorder.Hotkey = Hotkey.TryParse(selected.Shortcut, out var old) ? old : null;
                return;
            }

            error.Visibility = Visibility.Collapsed;
            RadialState.Mutate(c, (_, sel) => sel.Shortcut = h?.ToString() ?? "", RadialChange.Profiles);
        };
        body.AddSpaced(new SettingsRow(null, "Shortcut", null, recorder), 13);
        body.Children.Add(error);

        // ---- mouse button
        var mouse = new PickerRow<string>(null, "Mouse button", null, [("off", "Off"), ("back", "Back side button"), ("forward", "Forward side button")], selected.MouseButton is "back" or "forward" ? selected.MouseButton : "off", v =>
            RadialState.Mutate(c, (list, sel) =>
            {
                if (v != "off")
                {
                    foreach (var other in list.Where(p => p.Id != sel.Id && p.MouseButton == v))
                    {
                        other.MouseButton = "off";
                    }
                }

                sel.MouseButton = v;
            }, RadialChange.Profiles));
        body.AddSpaced(mouse, 13);
        body.AddSpaced(Caption("Only extra mouse buttons work here. With a mouse without extra buttons, use the keyboard shortcut above. With a trackpad, you can also use the four-finger tap below."), 8);
        if (selected.MouseButton != "off")
        {
            if (selected.MouseButton is "back" or "forward")
            {
                body.AddSpaced(Caption("While this is on, that button no longer goes back or forward in apps. If you navigate with it, leave this off."), 8);
            }

            body.AddSpaced(new RadialButtonTestRow(service, selected.MouseButton), 10);
            body.AddSpaced(Caption("If nothing lights up, Windows is not sending that button to apps. Mice that come with their own software often turn the side buttons into something else. Change it back there, or give the button a key combination and use that combination above."), 6);
        }

        // ---- four-finger tap
        body.AddSpaced(new SettingsRow(null, "Open with a four-finger tap", null, new MacSwitch(selected.TrackpadTap, SwitchSize.Regular, on =>
            RadialState.Mutate(c, (list, sel) =>
            {
                if (on)
                {
                    foreach (var other in list)
                    {
                        other.TrackpadTap = false;
                    }
                }

                sel.TrackpadTap = on;
            }, RadialChange.Profiles))), 13);
        body.AddSpaced(Caption("Precision touchpads only. Windows may also run its own four-finger gesture, set in Settings > Bluetooth & devices > Touchpad."), 6);
        if (selected.TrackpadTap && c.App.Get<bool>("middleClickEnabled") && c.App.Get<int>("middleClickTapFingers") > 0)
        {
            body.AddSpaced(UiKit.Text("Middle click already uses the four-finger tap, so the tap does not open this wheel.", 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true), 6);
        }

        return card;
    }

    private static List<RadialItem> RadialClone(List<RadialItem> items) => items.Select(i => i.Clone(newId: true)).ToList();

    private static string? ValidateShortcut(ISettingsContext c, Hotkey? hotkey, RadialProfile selected, List<RadialProfile> profiles)
    {
        if (hotkey is null)
        {
            return null;
        }

        if (hotkey.Modifiers == HotkeyModifiers.None && hotkey.Key is not (>= Key.F1 and <= Key.F24))
        {
            return "Use at least Ctrl, Alt or Win with a key.";
        }

        var text = hotkey.ToString();
        var other = profiles.FirstOrDefault(p => p.Id != selected.Id && p.Shortcut == text);
        if (other is not null)
        {
            return $"This shortcut is already used by {other.DisplayName}.";
        }

        if (c.Shortcuts.FindConflict(hotkey, "radialMenu") is { } role)
        {
            return $"This shortcut is already used by {role}.";
        }

        if (selected.Shortcut != text && c.Hotkeys.TryRegister(hotkey, () => { }, out var probe))
        {
            probe.Dispose();
        }
        else if (selected.Shortcut != text)
        {
            return "Nothing was captured. Windows or another app already uses that combination. Try another one.";
        }

        return null;
    }

    private static TextBlock Caption(string text) => UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);

    private static Button IconButton(string glyph, string tooltip)
    {
        var button = UiKit.Button("", ButtonKind.Plain, 13);
        button.Content = UiKit.Icon(glyph, 14, ThemeKeys.TextPrimary);
        button.ToolTip = tooltip;
        button.Margin = new Thickness(6, 0, 0, 0);
        button.Padding = new Thickness(7, 5, 7, 5);
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static FrameworkElement Swatch(ISettingsContext c, string id, bool selected)
    {
        var color = RadialColors.Resolve(id, ThemeManager.IsLight);
        var brush = new SolidColorBrush(color);
        var grid = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, ToolTip = RadialColors.Name(id), Background = Brushes.Transparent };
        grid.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = brush });
        if (selected)
        {
            grid.Children.Add(new Ellipse { Width = 12, Height = 12, Stroke = new SolidColorBrush(Color.FromArgb(ThemeManager.IsLight ? (byte)230 : (byte)204, 255, 255, 255)), StrokeThickness = 2 });
            grid.Children.Add(new Ellipse { Width = 20, Height = 20, Stroke = brush, StrokeThickness = 1.5 });
        }

        grid.MouseLeftButtonUp += (_, _) => RadialState.Mutate(c, (_, sel) => sel.Color = id, RadialChange.Profiles);
        return grid;
    }
}

/// <summary>The "Button test" row: shows whether Windows delivers the chosen side button to WinKnife.</summary>
internal sealed class RadialButtonTestRow : Grid
{
    private readonly IRadialMenuService? _service;
    private readonly string _expected;
    private readonly TextBlock _icon;
    private readonly TextBlock _status;

    public RadialButtonTestRow(IRadialMenuService? service, string expected)
    {
        _service = service;
        _expected = expected;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _icon = UiKit.Icon("", 14, ThemeKeys.Orange);
        _icon.Margin = new Thickness(0, 0, 8, 0);
        Children.Add(_icon);
        var label = UiKit.Text("Button test", 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        SetColumn(label, 1);
        Children.Add(label);
        _status = UiKit.Text("", 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        SetColumn(_status, 2);
        Children.Add(_status);
        Refresh();
        Loaded += (_, _) =>
        {
            _service?.SetButtonTest(true);
            if (_service is not null)
            {
                _service.ButtonSeen += Refresh;
            }

            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (_service is not null)
            {
                _service.ButtonSeen -= Refresh;
                _service.SetButtonTest(false);
            }
        };
    }

    private void Refresh()
    {
        string text;
        string glyph;
        string brush;
        if (_service is null || !_service.WatchingMouse)
        {
            (glyph, text, brush) = ("", "WinKnife cannot watch the mouse right now", ThemeKeys.Orange);
        }
        else if (_service.LastMouseButtonSeen is null)
        {
            (glyph, text, brush) = ("", "Press the button now", ThemeKeys.TextSecondary);
        }
        else if (_service.LastMouseButtonSeen == _expected)
        {
            (glyph, text, brush) = ("", "WinKnife sees this button", ThemeKeys.Green);
        }
        else
        {
            (glyph, text, brush) = ("", "That was a different button", ThemeKeys.Orange);
        }

        _icon.Text = glyph;
        _icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, brush == ThemeKeys.TextSecondary ? ThemeKeys.TextSecondary : brush);
    }
}

/// <summary>Settings &gt; Radial menu, card 4: the visual canvas, the add button and the list editor.</summary>
public sealed class RadialMenuActionsSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "radial-menu";

    /// <inheritdoc />
    public string PageId => SettingsPages.RadialMenu;

    /// <inheritdoc />
    public string? Anchor => "radialActions";

    /// <inheritdoc />
    public string? Title => "Actions";

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Add action", "Show as list", "Hide list", "Reset", "canvas", "submenu"];

    /// <inheritdoc />
    public int Order => 40;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext context)
    {
        RadialState.Ctx ??= context;
        return new RadialLiveView(() => Build(context), c => c is RadialChange.Items or RadialChange.Profiles);
    }

    private static UIElement Build(ISettingsContext c)
    {
        var profile = RadialState.Selected(c);
        var level = RadialState.Level(profile);
        var card = new SettingsCard("Actions");
        card.Add(new RadialVisualCanvas(c));
        card.AddCaption("Click a button to choose what it starts, or to remove it. Drag a button to move it.");
        if (level.Count < RadialModel.MaxItems)
        {
            var add = UiKit.Button("Add action", ButtonKind.Bordered, 12.5, "");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) => RadialItemOps.Add(add, c);
            card.Add(add);
        }

        // ---- disclosure with the list
        var header = new Border { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.Children.Add(UiKit.Text(RadialState.ListOpen ? "Hide list" : "Show as list", 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        var chevron = UiKit.Icon("", 10, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new RotateTransform(RadialState.ListOpen ? 90 : 0);
        Grid.SetColumn(chevron, 1);
        headerRow.Children.Add(chevron);
        header.Child = headerRow;
        header.MouseLeftButtonUp += (_, _) =>
        {
            RadialState.ListOpen = !RadialState.ListOpen;
            RadialState.Raise(RadialChange.Items);
        };
        card.Add(header);
        if (RadialState.ListOpen)
        {
            card.Add(BuildList(c, profile, level));
        }

        card.AddCaption(level.Count >= RadialModel.MaxItems ? "A wheel holds up to 12 actions." : RadialStrings.HubDescription);
        return card;
    }

    private static UIElement BuildList(ISettingsContext c, RadialProfile profile, List<RadialItem> level)
    {
        var list = new StackPanel();
        if (RadialState.SubmenuName(profile) is { } name)
        {
            var back = UiKit.Button("Back  " + name, ButtonKind.Plain, 12.5, "", RadialItemOps.Back);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(back);
        }

        if (level.Count == 0)
        {
            list.Children.Add(UiKit.Text("No actions here yet.", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            return list;
        }

        var rows = new StackPanel();
        Grid? dragging = null;
        foreach (var item in level)
        {
            var row = BuildRow(c, item);
            var grip = (TextBlock)row.Children[0];
            grip.MouseLeftButtonDown += (_, e) =>
            {
                dragging = row;
                row.Opacity = 0.45;
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (_, e) =>
            {
                if (dragging is null || !grip.IsMouseCaptured)
                {
                    return;
                }

                var y = e.GetPosition(rows).Y;
                var target = 0;
                var acc = 0.0;
                foreach (UIElement child in rows.Children)
                {
                    acc += ((FrameworkElement)child).ActualHeight;
                    if (y > acc - ((FrameworkElement)child).ActualHeight / 2)
                    {
                        target++;
                    }
                }

                target = Math.Clamp(target, 0, rows.Children.Count - 1);
                var current = rows.Children.IndexOf(dragging);
                if (target != current)
                {
                    rows.Children.Remove(dragging);
                    rows.Children.Insert(Math.Min(target, rows.Children.Count), dragging);
                }
            };
            grip.MouseLeftButtonUp += (_, _) =>
            {
                if (dragging is null)
                {
                    return;
                }

                grip.ReleaseMouseCapture();
                dragging.Opacity = 1;
                var order = rows.Children.Cast<FrameworkElement>().Select(r => (string)r.Tag).ToList();
                dragging = null;
                RadialState.MutateLevel(c, l =>
                {
                    var sorted = order.Select(id => l.First(x => x.Id == id)).ToList();
                    l.Clear();
                    l.AddRange(sorted);
                });
            };
            rows.Children.Add(row);
        }

        list.Children.Add(rows);
        return list;
    }

    private static Grid BuildRow(ISettingsContext c, RadialItem item)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4), Tag = item.Id, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = UiKit.Icon("", 12, ThemeKeys.TextTertiary);
        grip.Cursor = Cursors.SizeAll;
        grip.Background = Brushes.Transparent;
        row.Children.Add(grip);

        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var icon = RadialWheelWindow.MakeIcon(item, ThemeManager.IsLight, null, 13, 26);
        var usesImage = icon is Image;
        if (usesImage)
        {
            badge.Child = icon;
        }
        else
        {
            badge.Background = new LinearGradientBrush([new GradientStop(Color.FromRgb(0x3A, 0x3F, 0x4C), 0), new GradientStop(Color.FromRgb(0x14, 0x17, 0x1C), 1)], new Point(0, 0), new Point(1, 1));
            if (icon is TextBlock tb)
            {
                tb.Foreground = Brushes.White;
            }

            badge.Child = icon;
        }

        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        text.Children.Add(UiKit.Text(RadialModel.DisplayName(item), 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text(RadialKinds.Label(item.Kind), 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        text.MouseLeftButtonUp += (_, _) => RadialItemOps.Edit(row, c, item.Id);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        if (item.Kind == RadialKinds.Submenu)
        {
            var edit = UiKit.Button("Edit actions >", ButtonKind.Plain, 11.5, onClick: () => RadialItemOps.DrillInto(c, item.Id));
            edit.ToolTip = "Edit actions";
            Grid.SetColumn(edit, 3);
            row.Children.Add(edit);
        }

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) => RadialItemOps.Remove(c, item.Id);
        menu.Items.Add(remove);
        row.ContextMenu = menu;
        return row;
    }
}
