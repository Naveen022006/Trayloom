// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>Settings > Dynamic Island: header with the master switch, tab row and the five tabs (spec 02d).</summary>
public sealed class IslandSettingsSection : ISettingsSection
{
    public string? FeatureId => "notch";

    public string PageId => SettingsPages.Island;

    public string? Anchor => null;

    public string? Title => Loc.T("Dynamic Island");

    public int Order => 0;

    public IReadOnlyList<string> Keywords =>
    [
        "notch", "capsule", "layout", "content", "activity", "behavior", "companion", "mascot", "hover", "quick access", "explore", "lock screen",
        "timer", "calendar", "clipboard", "scratchpad", "watch", "gestures", "display", "outline", "size",
    ];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        K.RegisterDefaults(context.App);
        return new IslandSettingsView(context);
    }
}

internal sealed class IslandSettingsView : StackPanel
{
    private static int _tab;
    private static string? _pendingModule;

    private readonly ISettingsContext _ctx;
    private readonly StackPanel _body = new();

    public IslandSettingsView(ISettingsContext ctx)
    {
        _ctx = ctx;
        var app = ctx.App;
        if (ctx.RequestedAnchor == "companion" && ctx.Runtime.IsAvailable("notch-mascot"))
        {
            _tab = 4;
        }
        else if (ctx.RequestedAnchor is "layout" or "activity" or "behavior")
        {
            _tab = ctx.RequestedAnchor switch { "activity" => 2, "behavior" => 3, _ => 0 };
        }
        else if (ctx.RequestedAnchor is { } a && a.StartsWith("content", StringComparison.Ordinal))
        {
            _tab = 1;
            if (a.StartsWith("content-", StringComparison.Ordinal))
            {
                _pendingModule = a[8..];
            }
        }

        // header: description + master switch
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var desc = UiKit.Text(Strings.Description, 13, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        desc.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(desc);
        var master = new MacSwitch(app.Get<bool>(K.Enabled), SwitchSize.Regular, on => app.Set(K.Enabled, on)) { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        master.ToolTip = Strings.Enable;
        System.Windows.Automation.AutomationProperties.SetName(master, Strings.Enable);
        Grid.SetColumn(master, 1);
        head.Children.Add(master);
        Children.Add(head);
        // tab row
        var tabs = new List<string> { Strings.Layout, Strings.Content, Strings.Activity, Strings.Behavior };
        var companion = ctx.Runtime.IsAvailable("notch-mascot");
        if (companion)
        {
            tabs.Add(Strings.Companion);
        }

        if (_tab >= tabs.Count)
        {
            _tab = 0;
        }

        var row = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var seg = new SegmentedControl(tabs, _tab, true, 13);
        row.Children.Add(seg);
        var open = new Border { Width = 32, Height = 28, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = Strings.Open, Child = UiKit.Icon("", 13, ThemeKeys.TextPrimary) };
        open.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        open.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        if (open.Child is TextBlock ot)
        {
            ot.HorizontalAlignment = HorizontalAlignment.Center;
            ot.VerticalAlignment = VerticalAlignment.Center;
        }

        open.MouseLeftButtonUp += (_, _) =>
        {
            if (app.Get<bool>(K.Enabled))
            {
                IslandCore.Current?.Window?.Open(null, false, true);
            }
        };
        void Gate() => open.Opacity = app.Get<bool>(K.Enabled) ? 1 : 0.4;
        Gate();
        app.Changed += k => { if (k == K.Enabled) { Dispatcher.BeginInvoke(Gate); } };
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        Children.Add(row);
        Children.Add(_body);
        _body.Margin = new Thickness(0, 20, 0, 0);
        seg.SelectionChanged += i =>
        {
            _tab = i;
            Build();
        };
        Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        FrameworkElement view = _tab switch
        {
            0 => SettingsTabs.Layout(_ctx),
            1 => new ContentEditor(_ctx, _pendingModule),
            2 => SettingsTabs.Activity(_ctx, Build),
            3 => SettingsTabs.Behavior(_ctx, Build),
            _ => new CompanionPage(_ctx),
        };
        _pendingModule = null;
        _body.Children.Add(view);
    }
}

internal static class SettingsTabs
{
    // ================================================================== Layout

    public static FrameworkElement Layout(ISettingsContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.AddSpaced(new LayoutEditor(c), 0);
        var rebuild = () => c.RequestRebuild();

        // Size
        var size = new SettingsCard(Loc.T("Size"));
        var preset = app.Get<string>(K.Size);
        if (preset is not ("compact" or "spacious" or "custom"))
        {
            preset = "spacious";
        }

        size.Add(SUi.TileRow(3, 10,
            SUi.ChoiceTile("", Loc.T("Compact"), preset == "compact", () => { app.Set(K.Size, "compact"); rebuild(); }),
            SUi.ChoiceTile("", Loc.T("Spacious"), preset == "spacious", () => { app.Set(K.Size, "spacious"); rebuild(); }),
            SUi.ChoiceTile("", Loc.T("Custom"), preset == "custom", () => { app.Set(K.Size, "custom"); rebuild(); })));
        if (preset == "custom")
        {
            size.Add(SUi.SliderLine(Loc.T("Width"), 360, 600, app.Get<double>(K.CustomWidth), 10, v => ((int)v).ToString(), v => app.Set(K.CustomWidth, v)));
            size.Add(SUi.SliderLine(Loc.T("Maximum height"), 260, 640, app.Get<double>(K.CustomHeight), 10, v => ((int)v).ToString(), v => app.Set(K.CustomHeight, v)));
            size.AddCaption(Loc.T("Controls stay compact. Longer lists use up to this height."));
        }

        size.Add(SUi.SwitchRow(app, K.Translucent, "", Loc.T("Translucent background"), Loc.T("When open, Dynamic Island blurs what is behind it with the system’s own material.")));
        stack.AddSpaced(size, 20);

        // Displays without a notch
        var silhouette = app.Get<string>(K.Silhouette) == "notch" ? "notch" : "capsule";
        var shape = new SettingsCard(Loc.T("Displays without a notch"));
        shape.Add(SUi.TileRow(2, 10, SilhouetteTile(Loc.T("Capsule"), true, silhouette == "capsule", () => { app.Set(K.Silhouette, "capsule"); rebuild(); }), SilhouetteTile(Loc.T("Notch"), false, silhouette == "notch", () => { app.Set(K.Silhouette, "notch"); rebuild(); })));
        if (app.Get<bool>(K.Enabled) && app.Get<string>(K.Display) == "builtIn" && System.Windows.Forms.Screen.AllScreens.Length == 0)
        {
            shape.AddCaption(Loc.T("With the Display chosen on the Behavior tab, Dynamic Island doesn’t appear on these displays."));
            shape.Add(UiKit.Button(Loc.T("Show on all displays"), ButtonKind.Bordered, 12, null, () => { app.Set(K.Display, "all"); rebuild(); }));
        }

        stack.AddSpaced(shape, 20);

        // Capsule fit
        if (silhouette == "capsule")
        {
            var fit = new SettingsCard(Loc.T("Capsule fit"));
            string Signed(double v, int d) => (v > 0 ? "+" : "") + v.ToString(d == 0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
            fit.Add(SUi.SliderLine(Loc.T("Width"), -40, 40, app.Get<double>(K.CapsuleFitWidth), 2, v => Signed(v, 0), v => app.Set(K.CapsuleFitWidth, v)));
            fit.Add(SUi.SliderLine(Loc.T("Height"), -4, 4, app.Get<double>(K.CapsuleFitHeight), 1, v => Signed(v, 0), v => app.Set(K.CapsuleFitHeight, v)));
            // the drop applies on whichever edge the island rests on
            var atBottom = IsAtBottom(app);
            fit.Add(SUi.SliderLine(atBottom ? Loc.T("Distance from bottom") : Loc.T("Distance from top"), 0, 20, app.Get<double>(K.CapsuleFitDrop), 1, v => Signed(v, 0), v => app.Set(K.CapsuleFitDrop, v)));
            fit.AddCaption(atBottom
                ? Loc.T("Resizes the capsule and raises it from the bottom of the screen on displays without a notch.")
                : Loc.T("Resizes the capsule and lowers it from the top of the screen on displays without a notch."));
            stack.AddSpaced(fit, 20);
        }

        var outline = new SettingsCard();
        outline.Add(SUi.SwitchRow(app, K.Outline, "", Loc.T("Show outline")));
        stack.AddSpaced(outline, 20);
        return stack;
    }

    private static Border SilhouetteTile(string title, bool capsule, bool selected, Action onClick)
    {
        var b = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        if (selected)
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            b.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            b.BorderBrush = Brushes.Transparent;
        }

        var stack = new StackPanel();
        var sample = new Grid { Width = 200, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
        var bar = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true };
        bar.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        sample.Children.Add(bar);
        var shapeEl = new Border { Width = capsule ? 55 : 68, Height = capsule ? 10 : 12, Background = Brushes.Black, CornerRadius = capsule ? new CornerRadius(5) : new CornerRadius(0, 0, 6, 6), VerticalAlignment = capsule ? VerticalAlignment.Center : VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        sample.Children.Add(shapeEl);
        stack.Children.Add(sample);
        var t = UiKit.Text(title, 11, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        b.Child = stack;
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    // ================================================================== Activity

    public static FrameworkElement Activity(ISettingsContext c, Action rebuildTab)
    {
        var app = c.App;
        var core = IslandCore.Current;
        var stack = new StackPanel();
        var rest = new SettingsCard(Loc.T("At rest"));
        var hasBattery = IslandNative.Power().HasBattery;
        var current = app.Get<string>(K.IdleContent);
        var choices = new List<FrameworkElement>();
        void Pick(string value)
        {
            app.Set(K.IdleContent, value);
            rebuildTab();
        }

        string Resting(string sel) => sel == "battery" && !hasBattery ? "none" : sel;
        var marked = Resting(current);
        var mascotRests = app.Get<bool>(K.MascotEnabled) && !app.Get<bool>(K.MascotHides) && c.Runtime.IsAvailable("notch-mascot");
        choices.Add(RestChoice(mascotRests ? Loc.T("Companion") : Loc.T("Nothing"), "", "none", marked == "none", current == "none", null, () => Pick("none")));
        if (hasBattery)
        {
            choices.Add(RestChoice(Loc.T("Battery"), "", "battery", marked == "battery", current == "battery", c.Runtime.IsAvailable("monitor-power") ? null : Loc.T("Enable “Power” in Features."), () => Pick("battery")));
        }

        choices.Add(RestChoice(Loc.T("Music"), G.Music, "music", marked == "music", current == "music", core is not null && !core.ModuleVisible("music") ? Loc.T("Show “Now Playing” on the Content tab.") : null, () => Pick("music")));
        if (core?.ModuleVisible("agents") == true || current == "agents")
        {
            choices.Add(RestChoice(Loc.T("AI limits"), G.Claude, "agents", marked == "agents", current == "agents", core is not null && !core.ModuleVisible("agents") ? Loc.T("Show “AI Agents” on the Content tab.") : null, () => Pick("agents")));
        }

        if (core?.ModuleVisible("screenTime") == true || current == "screenTime")
        {
            choices.Add(RestChoice(Loc.T("Screen time"), G.Clock, "screenTime", marked == "screenTime", current == "screenTime", core is not null && !core.ModuleVisible("screenTime") ? Loc.T("Show “Screen time” on the Content tab.") : null, () => Pick("screenTime")));
        }

        var grid = new UniformGrid { Columns = choices.Count };
        foreach (var ch in choices)
        {
            ch.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(ch);
        }

        rest.Add(grid);
        if (marked == "battery")
        {
            rest.Add(SUi.SwitchRow(app, K.LowBatteryTint, "", Loc.T("Turn red when low"), Loc.T("Shows the charge in red at or below this level while on battery."), _ => rebuildTab()));
            if (app.Get<bool>(K.LowBatteryTint))
            {
                var red = Math.Clamp(app.Get<int>(K.LowBatteryThreshold), 1, 99);
                rest.Add(SUi.SliderLine(Loc.T("Red level"), 1, 100, red, 1, v => $"{(int)v}%", v =>
                {
                    var r = Math.Clamp((int)v, 1, 99);
                    app.Set(K.LowBatteryThreshold, r);
                    if (app.Get<int>(K.LowBatteryEarlyThreshold) <= r)
                    {
                        app.Set(K.LowBatteryEarlyThreshold, Math.Min(100, r + 1));
                    }
                }, SettingsRow.TextInset));
                rest.Add(SUi.SwitchRow(app, K.LowBatteryEarly, "", Loc.T("Turn amber first"), Loc.T("Shows the charge in amber at or below a higher level, as an early warning."), _ => rebuildTab()));
                if (app.Get<bool>(K.LowBatteryEarly))
                {
                    rest.Add(SUi.SliderLine(Loc.T("Amber level"), 2, 100, Math.Max(red + 1, app.Get<int>(K.LowBatteryEarlyThreshold)), 1, v => $"{(int)v}%", v => app.Set(K.LowBatteryEarlyThreshold, Math.Max(Math.Clamp(app.Get<int>(K.LowBatteryThreshold), 1, 99) + 1, (int)v)), SettingsRow.TextInset));
                }

                rest.Add(SUi.SwitchRow(app, K.LowBatteryMenuBar, "", Loc.T("Also in the tray icon")));
            }
        }

        stack.AddSpaced(rest, 20);

        // Indicators
        var ind = new SettingsCard(Loc.T("Indicators"));
        var rt = c.Runtime;
        var cards = new List<FrameworkElement>();
        FrameworkElement Card(string glyph, string title, string key, bool available, string? reason, Action? fix = null)
            => SUi.ToggleCard(glyph, title, app.Get<bool>(key), available, reason, () => { app.Set(key, !app.Get<bool>(key)); rebuildTab(); }, fix, true);
        string Missing(string id) => Loc.F("Enable “{0}” in Features.", FeatureCatalog.Get(id)?.Title ?? id);
        cards.Add(Card(G.Speaker, Loc.T("Volume"), K.Volume, rt.IsAvailable("mixer"), rt.IsAvailable("mixer") ? null : Missing("mixer"), () => c.Shell.OpenFeatures("mixer")));
        var brightnessOk = rt.IsAvailable("brightness") && app.Get<bool>(K.BrightnessControl);
        cards.Add(Card(G.Brightness, Loc.T("Brightness"), K.Brightness, brightnessOk, !rt.IsAvailable("brightness") ? Missing("brightness") : (brightnessOk ? null : Loc.T("Enable “Control displays” in its settings.")), () => { if (!rt.IsAvailable("brightness")) { c.Shell.OpenFeatures("brightness"); } else { c.Shell.OpenSettings("energy#brightness"); } }));
        cards.Add(Card("", Loc.T("Keyboard light"), K.KeyboardLight, false, Strings.KeyboardBacklightUnavailable));
        cards.Add(Card(G.Mic, Loc.T("Microphone"), K.Microphone, rt.IsAvailable("mic-mute"), rt.IsAvailable("mic-mute") ? null : Missing("mic-mute"), () => c.Shell.OpenFeatures("mic-mute")));
        if (hasBattery)
        {
            cards.Add(Card(G.Battery, Loc.T("Battery"), K.Battery, rt.IsAvailable("monitor-power"), rt.IsAvailable("monitor-power") ? null : Missing("monitor-power"), () => c.Shell.OpenFeatures("monitor-power")));
        }

        var accOk = rt.IsAvailable("notch-accessories") && rt.IsAvailable("monitor-power");
        cards.Add(Card(G.Headphones, Loc.T("Accessory alerts"), K.Accessories, accOk, accOk ? null : (!rt.IsAvailable("notch-accessories") ? Missing("notch-accessories") : Missing("monitor-power")), () => c.Shell.OpenFeatures(rt.IsAvailable("notch-accessories") ? "monitor-power" : "notch-accessories")));
        var clipOk = rt.IsAvailable("clipboard-history") && app.Get<bool>(K.ClipboardHistoryEnabled) && (core?.ModuleVisible("clipboard") ?? false);
        cards.Add(Card(G.Clipboard, Loc.T("Clipboard"), K.Clipboard, clipOk, !rt.IsAvailable("clipboard-history") ? Missing("clipboard-history") : (!app.Get<bool>(K.ClipboardHistoryEnabled) ? Loc.T("Enable “Save clipboard history” in its settings.") : (clipOk ? null : Loc.T("Show “Clipboard” on the Content tab.")))));
        var capOk = rt.IsAvailable("screenshot") && (core?.ModuleVisible("captures") ?? false);
        cards.Add(Card(G.Camera, Loc.T("Captures"), K.Capture, capOk, !rt.IsAvailable("screenshot") ? Missing("screenshot") : (capOk ? null : Loc.T("Show “Recent captures” on the Content tab."))));
        var trackOk = core?.ModuleVisible("music") ?? false;
        cards.Add(Card(G.Music, Loc.T("New track"), K.TrackChange, trackOk, trackOk ? null : Loc.T("Show “Now Playing” on the Content tab.")));
        cards.Add(Card(G.Keyboard, Loc.T("Input source"), K.InputSource, true, null));
        ind.Add(SUi.CardGrid(cards, 116, 700));
        ind.AddDivider();
        ind.Add(SUi.SwitchRow(app, K.MicInUse, "", Loc.T("Show when an app uses the microphone"), Loc.T("While an app records from the microphone, the island shows a pulsing orange microphone with the app’s name, as the indicator on iPhone and Mac does.")));
        if (app.Get<bool>(K.Accessories))
        {
            ind.AddCaption(Loc.T("Show connected accessories and warn once when their battery falls to 20%."));
        }

        stack.AddSpaced(ind, 20);

        // Lock screen
        var lockCard = new SettingsCard(Loc.T("Lock Screen"));
        lockCard.Add(SUi.SwitchRow(app, K.LockScreen, "", Loc.T("Show on the Lock Screen"), Loc.T("Windows doesn’t allow apps to draw on the lock screen. Trayloom shows a greeting when you unlock instead.")));
        lockCard.AddDivider();
        lockCard.Add(SUi.SwitchRow(app, K.LockSounds, "", Loc.T("Lock and unlock sounds"), Loc.T("Play a sound when your PC locks and unlocks.")));
        stack.AddSpaced(lockCard, 20);
        return stack;
    }

    private static FrameworkElement RestChoice(string title, string glyph, string value, bool marked, bool saved, string? reason, Action onClick)
    {
        var col = new StackPanel();
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(0, 14, 0, 14), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        if (marked)
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            b.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            b.BorderBrush = Brushes.Transparent;
        }

        if (reason is not null)
        {
            b.Opacity = 0.45;
        }

        var inner = new StackPanel();
        var capsule = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (value == "none")
        {
            row.Children.Add(new Border { Width = 50, Height = 12 });
        }
        else
        {
            row.Children.Add(D.I(glyph, 11, Brushes.White));
            row.Children.Add(new Border { Width = 20, Height = 12, Background = Brushes.Black, CornerRadius = new CornerRadius(4), Margin = new Thickness(14, 0, 14, 0) });
            row.Children.Add(value switch
            {
                "battery" => D.T("76%", 9, FontWeights.Medium),
                "agents" => D.T("62%", 9, FontWeights.Medium),
                "screenTime" => D.T("3h 12m", 9, FontWeights.Medium),
                _ => (FrameworkElement)D.I("", 9, Brushes.White),
            });
        }

        capsule.Child = row;
        inner.Children.Add(capsule);
        var t = UiKit.Text(title, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 14, 0, 0);
        inner.Children.Add(t);
        b.Child = inner;
        b.MouseLeftButtonUp += (_, _) => onClick();
        col.Children.Add(b);
        if (reason is not null)
        {
            var link = UiKit.Text(reason, 10, FontWeights.Normal, ThemeKeys.Accent, true);
            link.TextAlignment = TextAlignment.Center;
            link.Margin = new Thickness(0, 4, 0, 0);
            col.Children.Add(link);
        }

        _ = saved;
        return col;
    }

    // ================================================================== Behavior

    public static FrameworkElement Behavior(ISettingsContext c, Action rebuildTab)
    {
        var app = c.App;
        var rt = c.Runtime;
        var stack = new StackPanel();
        var opening = new SettingsCard(Loc.T("Opening"));
        var hover = app.Get<bool>(K.OpenOnHover);
        var expands = app.Get<bool>(K.HoverExpands);
        var hideUntil = app.Get<bool>(K.HideUntilHover);
        void Mode(bool h, bool e, bool u)
        {
            app.Set(K.OpenOnHover, h);
            app.Set(K.HoverExpands, e);
            app.Set(K.HideUntilHover, u);
            rebuildTab();
        }

        opening.Add(SUi.TileRow(2, 8,
            SUi.ChoiceTile("", Loc.T("Click to open"), !hover, () => Mode(false, expands, false)),
            SUi.ChoiceTile("", Loc.T("Preview on hover"), hover && !expands && !hideUntil, () => Mode(true, false, false)),
            SUi.ChoiceTile("", Loc.T("Expand on hover"), hover && expands && !hideUntil, () => Mode(true, true, false)),
            SUi.ChoiceTile("", Loc.T("Hidden until hover"), hover && hideUntil, () => Mode(true, true, true))));
        if (hover && hideUntil)
        {
            opening.Add(SUi.SwitchRow(app, K.HiddenShowsActivity, "", Loc.T("Show activity and indicators"), null, _ => rebuildTab()));
            if (app.Get<bool>(K.HiddenShowsActivity))
            {
                opening.AddCaption(Loc.T("Comes into view while an activity runs or an indicator shows, then hides again."));
            }
        }

        if (hover || (app.Get<bool>(K.HideInFullscreen) && app.Get<bool>(K.HoverInFullscreen)))
        {
            opening.Add(SUi.SliderLine(Loc.T("Activation time"), 0.10, 1.00, app.Get<double>(K.HoverDelay), 0.05, v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s", v => app.Set(K.HoverDelay, Math.Round(v, 2))));
            opening.AddCaption(Loc.T("Keep the pointer over the island for this long to open it."));
            if (app.Get<bool>(K.HoverExpands))
            {
                opening.Add(SUi.SliderLine(Loc.T("Closing time"), 0.10, 2.00, app.Get<double>(K.CloseDelay), 0.05, v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s", v => app.Set(K.CloseDelay, Math.Round(v, 2))));
                opening.AddCaption(Loc.T("Wait this long after the pointer leaves the island before closing it."));
            }
        }

        opening.Add(SUi.SwitchRow(app, K.ShortcutEnabled, "", Loc.T("Global shortcut"), null, _ => rebuildTab()));
        if (app.Get<bool>(K.ShortcutEnabled))
        {
            var recorder = new HotkeyRecorder { Hotkey = c.Shortcuts.GetHotkey("notch"), Width = 140 };
            var error = UiKit.Text("", 11.5, FontWeights.Normal, ThemeKeys.Orange, true);
            recorder.HotkeyChanged += (_, hk) =>
            {
                var r = c.Shortcuts.TrySet("notch", hk);
                error.Text = r.Error ?? "";
                var st = c.Shortcuts.GetState("notch");
                if (st.Problem is not null && r.Error is null)
                {
                    error.Text = Loc.T("Windows rejected this shortcut. Choose another one.");
                }
            };
            var line = new SettingsRow(null, Loc.T("Shortcut"), null, recorder);
            SUi.Indent(line);
            opening.Add(line);
            var st0 = c.Shortcuts.GetState("notch");
            if (st0.IsActive && !st0.IsRegistered)
            {
                error.Text = Loc.T("Windows rejected this shortcut. Choose another one.");
            }

            opening.Add(SUi.Indent(error));
        }

        // at the bottom the pages open upward, so the wheel directions swap (read from the stored position, so it also works while the island is off)
        var gesturesCaption = IsAtBottom(app)
            ? Loc.T("Scroll up to open. Scroll down over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling.")
            : Loc.T("Scroll down to open. Scroll up over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling.");
        opening.Add(new FeatureSwitchRow(rt, c.Shell, "notch-gestures","", Loc.T("Dynamic Island Gestures"), app.Get<bool>(K.Gestures) ? gesturesCaption : null, app.Get<bool>(K.Gestures), on => { app.Set(K.Gestures, on); rebuildTab(); }, Loc.T("Enable “Dynamic Island Gestures” in Features.")));
        var modules = IslandCore.Current?.Modules() ?? [];
        var reopenItems = new List<(string Value, string Label)> { ("", Loc.T("Last page")), ("appPanel", Loc.T("Open app panel")), ("explore", Loc.T("Explore")) };
        reopenItems.AddRange(modules.Select(m => (m.Id, m.Title)));
        var home = app.Get<bool>(K.ReturnHome) ? app.Get<string>(K.HomeModule) : "";
        opening.Add(new PickerRow<string>("", Loc.T("When reopening"), null, reopenItems, reopenItems.Any(i => i.Value == home) ? home : "controls", v =>
        {
            app.Set(K.ReturnHome, v.Length > 0);
            if (v.Length > 0)
            {
                app.Set(K.HomeModule, v);
            }
        }, 160));
        opening.Add(SUi.SwitchRow(app, K.OpensActivity, "", Loc.T("Open the visible activity"), Loc.T("When the closed island shows music, a timer or another activity, it opens on that page instead.")));
        stack.AddSpaced(opening, 20);

        // Display
        var display = new SettingsCard(Loc.T("Display"));
        display.Add(SUi.SwitchRow(app, K.HideInFullscreen, "", Loc.T("Hide content in full screen"), null, _ => rebuildTab()));
        if (app.Get<bool>(K.HideInFullscreen))
        {
            display.Add(SUi.Indent(SUi.SwitchRow(app, K.HoverInFullscreen, "", Loc.T("Hidden until hover in full screen"), null, _ => rebuildTab())));
        }

        var disp = app.Get<string>(K.Display);
        if (disp is not ("automatic" or "builtIn" or "main" or "pointer" or "all" or IslandPlacement.SpecificDisplay))
        {
            disp = "automatic";
        }

        // Own() keeps the live refresh (below) from rebuilding the tab a second time for a change this page makes itself
        var own = false;
        void Own(Action write)
        {
            own = true;
            try
            {
                write();
            }
            finally
            {
                own = false;
            }
        }

        void SetDisp(string v)
        {
            Own(() =>
            {
                // "specific" starts on the display the island is on now; the id goes first so the island never flashes to the main display
                if (v == IslandPlacement.SpecificDisplay && app.Get<string>(K.DisplayId).Length == 0 && ResolveMonitor(app) is { } here)
                {
                    app.Set(K.DisplayId, here.Key);
                }

                app.Set(K.Display, v);
            });
            rebuildTab();
        }

        display.Add(SUi.TileRow(2, 8,
            SUi.ChoiceTile("", Loc.T("Automatic"), disp == "automatic", () => SetDisp("automatic")),
            SUi.ChoiceTile("", Loc.T("Built-in display"), disp == "builtIn", () => SetDisp("builtIn")),
            SUi.ChoiceTile("", Loc.T("Main display"), disp == "main", () => SetDisp("main")),
            SUi.ChoiceTile("", Loc.T("Follow pointer"), disp == "pointer", () => SetDisp("pointer")),
            SUi.ChoiceTile("", Loc.T("All displays"), disp == "all", () => SetDisp("all")),
            SUi.ChoiceTile(G.Display, Loc.T("Choose a display"), disp == IslandPlacement.SpecificDisplay, () => SetDisp(IslandPlacement.SpecificDisplay))));
        if (disp == IslandPlacement.SpecificDisplay)
        {
            var connected = IslandDisplays.Enumerate();
            var savedId = app.Get<string>(K.DisplayId);
            var match = IslandPlacement.FindByKey(connected, savedId);
            var items = connected.Select(m => (m.Key, DisplayLabel(m))).ToList();
            if (match is null)
            {
                // nothing to select while the saved display is unplugged: show the prompt instead of silently naming another display
                items.Insert(0, ("", Loc.T("Choose a display")));
            }

            display.Add(new PickerRow<string>("", Loc.T("Display"), null, items, match?.Key ?? "", v =>
            {
                if (v.Length > 0)
                {
                    Own(() => app.Set(K.DisplayId, v));
                    rebuildTab();
                }
            }, 300));
            if (savedId.Length > 0 && match is null)
            {
                display.AddCaption(Loc.T("This display isn’t connected. Dynamic Island shows on the main display until it’s back."));
            }
        }

        stack.AddSpaced(display, 20);

        stack.AddSpaced(PositionCard(app, Own, rebuildTab), 20);

        // the page follows the island when it is moved elsewhere (a drag, "Move island…", a dev action) or turned on or off
        var refreshPending = false;
        void OnSetting(string key)
        {
            if (own || refreshPending || key is not (K.Enabled or K.Display or K.DisplayId or K.Position or K.PositionsByDisplay))
            {
                return;
            }

            refreshPending = true;
            stack.Dispatcher.BeginInvoke(() =>
            {
                refreshPending = false;
                if (stack.IsLoaded)
                {
                    rebuildTab();
                }
            });
        }

        stack.Loaded += (_, _) => app.Changed += OnSetting;
        stack.Unloaded += (_, _) => app.Changed -= OnSetting;

        // Where things open
        var where = new SettingsCard(Loc.T("Where things open"));
        where.Add(SUi.DestinationRow(app, K.AppPanel, "", Loc.T("Open app panel"), Loc.T("Choose where clicking the Trayloom tray icon opens the app panel, on any display.")));
        where.AddDivider();
        where.Add(SUi.SwitchRow(app, K.HidesTrayIcon, "", Loc.T("Hide the tray icon"), Loc.T("Settings and the app panel open from Dynamic Island. The icon returns when the island is off or hidden in full screen, or when an update is ready or the microphone is muted.")));
        where.AddDivider();
        where.Add(SUi.DestinationRow(app, K.QuickPanel, "", Loc.T("Tools"), null, rt.IsAvailable("quick-launcher")));
        where.AddDivider();
        where.Add(SUi.DestinationRow(app, K.ClipboardWindow, G.Clipboard, Loc.T("Clipboard"), null, rt.IsAvailable("clipboard-history")));
        where.AddDivider();
        where.Add(SUi.DestinationRow(app, K.Shelf, G.Files, Loc.T("Files"), null, rt.IsAvailable("shelf")));
        where.AddDivider();
        where.Add(SUi.DestinationRow(app, K.CaptureControls, G.Camera, Loc.T("Captures"), null));
        where.AddDivider();
        where.Add(SUi.DestinationRow(app, K.Scratchpad, G.Note, Loc.T("Scratchpad"), null, rt.IsAvailable("scratchpad")));
        stack.AddSpaced(where, 20);

        var privacy = new SettingsCard(Loc.T("Privacy"));
        privacy.Add(SUi.SwitchRow(app, K.ShowInCaptures, G.Camera, Loc.T("Show in screenshots and videos")));
        stack.AddSpaced(privacy, 20);
        return stack;
    }

    // ================================================================== Position

    /// <summary>Card "Position": six presets, the distance from the edge, Move island / Reset position, Lock position (spec 02d, Behavior tab).</summary>
    private static SettingsCard PositionCard(IAppSettings app, Action<Action> own, Action rebuildTab)
    {
        var card = new SettingsCard(Loc.T("Position"));
        var monitor = ResolveMonitor(app);
        var frame = monitor is null ? (IslandFrame?)null : IslandPlacement.Frame(monitor, IslandPlacementStore.Load(app, monitor), SettingsZoom(app));
        var pos = frame?.Position ?? IslandPosition.Default;

        bool At(double x, IslandEdge edge) => pos.Edge == edge && Math.Abs(pos.X - x) < 0.005;
        void Preset(double x, IslandEdge edge)
        {
            if (monitor is null)
            {
                return;
            }

            // only the side and edge change; the distance from the edge is kept (the slider owns it)
            var current = IslandPlacementStore.Load(app, monitor);
            own(() => IslandPlacementStore.Save(app, monitor, current with { X = x, Edge = edge }, null));
            rebuildTab();
        }

        var presets = new (string Title, double X, IslandEdge Edge)[]
        {
            (Loc.T("Top left"), 0, IslandEdge.Top),
            (Loc.T("Top center"), 0.5, IslandEdge.Top),
            (Loc.T("Top right"), 1, IslandEdge.Top),
            (Loc.T("Bottom left"), 0, IslandEdge.Bottom),
            (Loc.T("Bottom center"), 0.5, IslandEdge.Bottom),
            (Loc.T("Bottom right"), 1, IslandEdge.Bottom),
        };
        var matched = false;
        var tiles = new List<FrameworkElement>();
        foreach (var p in presets)
        {
            var selected = monitor is not null && At(p.X, p.Edge);
            matched |= selected;
            var (x, edge) = (p.X, p.Edge);
            tiles.Add(PositionTile(p.Title, x, edge == IslandEdge.Bottom, selected, () => Preset(x, edge)));
        }

        card.Add(SUi.TileRow(3, 8, tiles.ToArray()));
        if (!matched)
        {
            card.AddCaption(Loc.T("Custom position"));
        }

        if (frame is { } f && Math.Floor(f.MaxOffset) >= 2 && monitor is not null)
        {
            var max = Math.Floor(f.MaxOffset / 2) * 2;
            card.Add(SUi.SliderLine(Loc.T("Distance from edge"), 0, max, Math.Round(pos.Offset / 2) * 2, 2, v => ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture), v =>
            {
                var current = IslandPlacementStore.Load(app, monitor);
                var next = current with { Offset = v };
                if (next != current)
                {
                    own(() => IslandPlacementStore.Save(app, monitor, next, null));
                }
            }));
        }

        var window = IslandCore.Current?.Window;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var move = UiKit.Button(Loc.T("Move island…"), ButtonKind.Bordered, 12, null, () => IslandCore.Current?.Window?.EnterMoveMode());
        move.IsEnabled = app.Get<bool>(K.Enabled) && window is not null;
        buttons.Children.Add(move);
        var reset = UiKit.Button(Loc.T("Reset position"), ButtonKind.Bordered, 12, null, () =>
        {
            own(() =>
            {
                if (IslandCore.Current?.Window is { } w)
                {
                    w.ResetPosition();
                }
                else
                {
                    IslandPlacementStore.Reset(app);
                }
            });
            rebuildTab();
        });
        reset.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(reset);
        card.Add(buttons);
        card.AddDivider();
        card.Add(SUi.SwitchRow(app, K.PositionLocked, "", Loc.T("Lock position"), Loc.T("Dragging the closed island doesn’t move it. “Move island…” still works.")));
        card.AddCaption(Loc.T("Drag the closed island to move it. Pages open away from the edge it rests on."));
        return card;
    }

    /// <summary>A preset tile: a small screen with the island drawn where the preset puts it.</summary>
    private static Border PositionTile(string title, double x, bool bottom, bool selected, Action onClick)
    {
        var b = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        if (selected)
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
            b.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            b.BorderBrush = Brushes.Transparent;
        }

        var stack = new StackPanel();
        var sample = new Grid { Height = 30, MaxWidth = 120 };
        var screen = new Border { CornerRadius = new CornerRadius(6) };
        screen.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        sample.Children.Add(screen);
        sample.Children.Add(new Border
        {
            Width = 26,
            Height = 7,
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(3.5),
            Margin = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = x <= 0 ? HorizontalAlignment.Left : x >= 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
        });
        stack.Children.Add(sample);
        var t = UiKit.Text(title, 11, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        b.Child = stack;
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    /// <summary>The display the island is on: the live window's, else the one the Display setting resolves to right now.</summary>
    private static IslandMonitor? ResolveMonitor(IAppSettings app)
    {
        try
        {
            if (IslandCore.Current?.Window?.CurrentMonitor is { } live)
            {
                return live;
            }

            var cursor = System.Windows.Forms.Cursor.Position;
            return IslandPlacement.Resolve(app.Get<string>(K.Display), app.Get<string>(K.DisplayId), IslandDisplays.Enumerate(), cursor.X, cursor.Y);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The stored position of the island's display rests on the bottom edge (read from the settings, so it holds while the island is off).</summary>
    private static bool IsAtBottom(IAppSettings app) =>
        ResolveMonitor(app) is { } m && IslandPlacementStore.Load(app, m).Edge == IslandEdge.Bottom;

    private static double SettingsZoom(IAppSettings app)
    {
        var z = app.Get(IslandWindow.ZoomKey, IslandWindow.DefaultZoom);
        return double.IsFinite(z) ? Math.Clamp(z, 0.9, 1.3) : IslandWindow.DefaultZoom;
    }

    /// <summary>"Name — W × H" in physical pixels, with the main display marked.</summary>
    private static string DisplayLabel(IslandMonitor m)
    {
        var label = $"{m.Name} — {m.Bounds.Width} × {m.Bounds.Height}";
        return m.IsPrimary ? $"{label} · {Loc.T("Main display")}" : label;
    }
}
