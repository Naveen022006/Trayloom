// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>Settings > Dynamic Island > Companion (spec 02d section 6).</summary>
internal sealed class CompanionPage : StackPanel
{
    private readonly ISettingsContext _c;
    private readonly MascotActor _actor = new();
    private readonly Border _stage = new() { Height = 104, CornerRadius = new CornerRadius(12), ClipToBounds = true, BorderThickness = new Thickness(1) };
    private readonly TextBlock _momentName = new();
    private readonly Border _momentPill = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    private MascotView? _view;
    private int _next;

    private static readonly (string Name, string Glyph, string[] Reactions)[] Moments =
    [
        ("A visit", "", ["celebrate"]), ("Timer started", "", ["ready"]), ("Time is up", "", ["surprised"]), ("Now Playing", "", ["groove"]),
        ("AI Agents", "", ["ready", "celebrate"]), ("Happening now", "", ["bounce"]), ("Download complete", "", ["celebrate"]), ("Download failed", "", ["confused"]),
        ("Screenshot", "", ["flash"]), ("Mic Mute on", "", ["hush"]), ("Keep awake", "", ["perk"]), ("Charging", "", ["love"]), ("Low battery", "", ["yawn"]), ("PC unlocked", "", ["wakeUp"]),
    ];

    public CompanionPage(ISettingsContext c)
    {
        _c = c;
        Build();
        Unloaded += (_, _) => { };
    }

    private IAppSettings App => _c.App;

    private MascotLook Look => new(App.Get<string>(K.MascotStyle), App.Get<string>(K.MascotShape), App.Get<string>(K.MascotPalette));

    private void Rebuild() => _c.RequestRebuild();

    private void Build()
    {
        Children.Clear();
        var enabled = App.Get<bool>(K.MascotEnabled);
        var islandOn = App.Get<bool>(K.Enabled) && _c.Runtime.IsAvailable("notch");
        // header
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var col = new StackPanel();
        col.Children.Add(UiKit.Text("Companion", 20, FontWeights.Bold, ThemeKeys.TextPrimary));
        var hint = UiKit.Text("A little friend who lives in the Dynamic Island. It rests there when nothing else is showing, and comes out over music and activities to say hello and react to what happens.", 13, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        hint.Margin = new Thickness(0, 4, 0, 0);
        col.Children.Add(hint);
        head.Children.Add(col);
        var sw = new MacSwitch(enabled, SwitchSize.Regular, on => { App.Set(K.MascotEnabled, on); _actor.Asleep = !on; _actor.Play(on ? "wakeUp" : "yawn", D.ReduceMotion); Rebuild(); }) { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sw, 1);
        head.Children.Add(sw);
        Children.Add(head);

        if (!islandOn)
        {
            var note = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0, 20, 0, 0) };
            note.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
            var nr = new Grid();
            nr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nr.Children.Add(UiKit.Text("The companion lives in the Dynamic Island.", 13, FontWeights.Normal, ThemeKeys.TextPrimary));
            var btn = UiKit.Button(_c.Runtime.IsAvailable("notch") ? "Enable Dynamic Island" : "Open Features", ButtonKind.Bordered, 12, null, () =>
            {
                if (_c.Runtime.IsAvailable("notch"))
                {
                    App.Set(K.Enabled, true);
                    Rebuild();
                }
                else
                {
                    _c.Shell.OpenFeatures("notch");
                }
            });
            Grid.SetColumn(btn, 1);
            nr.Children.Add(btn);
            note.Child = nr;
            Children.Add(note);
        }

        // stage card
        var stage = new SettingsCard { Margin = new Thickness(0, 20, 0, 0) };
        _stage.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        BuildStage(enabled);
        stage.Add(_stage);
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(UiKit.Text("See how it reacts", 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var say = UiKit.Button("Say Hi", ButtonKind.Bordered, 11.5, "", () => { _actor.Wake(); _actor.Play("celebrate", D.ReduceMotion); IslandCore.Current?.Mascot.PlayNow("celebrate"); });
        say.IsEnabled = enabled && islandOn;
        say.ToolTip = "Plays a visit in the Dynamic Island now.";
        Grid.SetColumn(say, 1);
        titleRow.Children.Add(say);
        stage.Add(titleRow);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, glyph, reactions) in Moments)
        {
            var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Opacity = enabled ? 1 : 0.45 };
            chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            var r = new StackPanel { Orientation = Orientation.Horizontal };
            r.Children.Add(UiKit.Icon(glyph, 10, ThemeKeys.TextPrimary));
            var t = UiKit.Text(name, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
            t.Margin = new Thickness(5, 0, 0, 0);
            r.Children.Add(t);
            chip.Child = r;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (!enabled)
                {
                    return;
                }

                PlayMoment(name, reactions);
            };
            chips.Children.Add(chip);
        }

        stage.Add(chips);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Opacity = enabled ? 1 : 0.45 };
        foot.Children.Add(UiKit.Icon("", 11, ThemeKeys.TextSecondary));
        var ft = SUi.Caption("Click to see it react. Rest the pointer on it to pet it. Left alone, it dozes off until you come back.");
        ft.Margin = new Thickness(6, 0, 0, 0);
        foot.Children.Add(ft);
        stage.Add(foot);
        Children.Add(stage);

        // appearance
        var app = new SettingsCard("Appearance") { Margin = new Thickness(0, 20, 0, 0) };
        var style = App.Get<string>(K.MascotStyle);
        app.Add(new SettingsRow("", "Style", null));
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
        styleRow.Children.Add(LookTile(new MascotLook("minimal", App.Get<string>(K.MascotShape), App.Get<string>(K.MascotPalette)), 30, "Minimal", 64, 42, 14, style == "minimal", () => SetLook(K.MascotStyle, "minimal")));
        styleRow.Children.Add(LookTile(new MascotLook("robot", "ball", App.Get<string>(K.MascotPalette)), 30, "Robot", 64, 42, 14, style == "robot", () => SetLook(K.MascotStyle, "robot")));
        app.Add(styleRow);
        if (style == "minimal")
        {
            var shape = App.Get<string>(K.MascotShape);
            app.Add(new SettingsRow("", "Shape", null, UiKit.Text(shape switch { "egg" => "Egg", "squircle" => "Rounded square", "pill" => "Pill", _ => "Ball" }, 12, FontWeights.Normal, ThemeKeys.TextSecondary)));
            var shapes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
            foreach (var (id, label) in new[] { ("ball", "Ball"), ("egg", "Egg"), ("squircle", "Rounded square"), ("pill", "Pill") })
            {
                shapes.Children.Add(LookTile(new MascotLook("minimal", id, App.Get<string>(K.MascotPalette)), 24, label, 58, 40, 10, shape == id, () => SetLook(K.MascotShape, id)));
            }

            app.Add(shapes);
        }

        var palette = App.Get<string>(K.MascotPalette);
        app.Add(new SettingsRow("", "Color", null, UiKit.Text(char.ToUpperInvariant(palette[0]) + palette[1..], 12, FontWeights.Normal, ThemeKeys.TextSecondary)));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
        foreach (var p in new[] { "pearl", "lemon", "peach", "rose", "lilac", "sky", "mint" })
        {
            var (light, shade) = MascotLook.Palette_(p);
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new LinearGradientBrush(light, shade, 90), BorderThickness = new Thickness(0.5), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) };
            var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Child = dot, ToolTip = char.ToUpperInvariant(p[0]) + p[1..] };
            if (p == palette)
            {
                ring.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }
            else
            {
                ring.BorderBrush = Brushes.Transparent;
            }

            var pid = p;
            ring.MouseLeftButtonUp += (_, _) => SetLook(K.MascotPalette, pid);
            swatches.Children.Add(ring);
        }

        app.Add(swatches);
        Children.Add(app);

        // behavior
        var beh = new SettingsCard("Behavior") { Margin = new Thickness(0, 20, 0, 0) };
        if (App.Get<string>(K.Silhouette) == "notch")
        {
            var side = App.Get<string>(K.MascotSide);
            beh.Add(new SettingsRow("", "Side of the camera", null));
            var sides = SUi.TileRow(2, 8, SUi.ChoiceTile("", "Left", side == "left", () => { App.Set(K.MascotSide, "left"); Rebuild(); }, 50), SUi.ChoiceTile("", "Right", side == "right", () => { App.Set(K.MascotSide, "right"); Rebuild(); }, 50));
            sides.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
            beh.Add(sides);
        }

        beh.Add(SUi.SwitchRow(App, K.MascotHides, "", "Hide when idle", "After half a minute with nothing going on, it hops into the island and the island goes back to its usual size. Visits and reactions still bring it out."));
        beh.Add(SUi.SwitchRow(App, K.MascotVisits, "", "Appear now and then", "Every few minutes it passes through the island with a short animation.", _ => Rebuild()));
        if (App.Get<bool>(K.MascotVisits))
        {
            var seg = new SegmentedControl(["Rarely", "Sometimes", "Often"], App.Get<string>(K.MascotFrequency) switch { "rare" => 0, "frequent" => 2, _ => 1 }, false, 12);
            seg.SelectionChanged += i => App.Set(K.MascotFrequency, i switch { 0 => "rare", 2 => "frequent", _ => "normal" });
            beh.Add(SUi.Indent(new SettingsRow("", "How often", null, seg)));
        }

        beh.Add(SUi.SwitchRow(App, K.MascotReactions, "", "React to what happens", "It comes out to react to music, timers, downloads, screenshots, the microphone, Keep awake and more."));
        Children.Add(beh);

        // command bar
        var cb = new SettingsCard("Command Bar") { Margin = new Thickness(0, 20, 0, 0) };
        cb.Add(new FeatureSwitchRow(_c.Runtime, _c.Shell, "command-bar", "", "Command Bar in the island", "With the shortcut, the Command Bar comes out of the island with the companion as its face. It reacts while you search.", App.Get<bool>(K.CommandBar), on => { App.Set(K.CommandBar, on); Rebuild(); }, "Enable “Command Bar” in Features."));
        if (_c.Runtime.IsAvailable("command-bar") && App.Get<bool>(K.CommandBar))
        {
            var seg = new SegmentedControl(["Drop", "Open island"], App.Get<string>(K.CommandBarStyle) == "island" ? 1 : 0, false, 12);
            seg.SelectionChanged += i => App.Set(K.CommandBarStyle, i == 1 ? "island" : "droplet");
            cb.Add(SUi.Indent(new SettingsRow("", "Opens as", null, seg)));
        }

        Children.Add(cb);
    }

    private void SetLook(string key, string value)
    {
        App.Set(key, value);
        _actor.Play("celebrate", D.ReduceMotion);
        Rebuild();
    }

    private Border LookTile(MascotLook look, double size, string tooltip, double w, double h, double radius, bool selected, Action click)
    {
        var tile = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = Brushes.Black, Margin = new Thickness(0, 4, 8, 4), Cursor = Cursors.Hand, ToolTip = tooltip, BorderThickness = new Thickness(2) };
        if (selected)
        {
            tile.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            tile.BorderBrush = Brushes.Transparent;
        }

        var actor = new MascotActor();
        var view = new MascotView(actor, size, look) { StepsActor = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tile.Child = view;
        tile.MouseLeftButtonUp += (_, _) => click();
        return tile;
    }

    private void BuildStage(bool enabled)
    {
        var g = new Grid();
        var wall = new LinearGradientBrush(Color.FromArgb(70, 80, 120, 210), Color.FromArgb(20, 80, 120, 210), new Point(0, 0), new Point(1, 1));
        g.Background = wall;
        var bar = new Border { Height = 24, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
        g.Children.Add(bar);
        var strip = new Border { Width = 223 * 0.9, Height = 24, Background = Brushes.Black, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Child = null };
        _view = new MascotView(_actor, 18, Look) { StepsActor = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _actor.Asleep = !enabled;
        if (!enabled)
        {
            _actor.SetMood("sleepy");
        }

        strip.Child = _view;
        g.Children.Add(strip);
        _momentPill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        _momentName.FontSize = 11;
        _momentName.FontWeight = FontWeights.SemiBold;
        _momentName.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
        _momentPill.Child = _momentName;
        g.Children.Add(_momentPill);
        _stage.Child = g;
        _stage.Cursor = Cursors.Hand;
        _stage.MouseLeftButtonUp += (_, _) =>
        {
            if (!enabled)
            {
                return;
            }

            var i = _next++ % (Moments.Length - 1) + 1;
            PlayMoment(Moments[i].Name, Moments[i].Reactions);
        };
        _stage.Opacity = enabled ? 1 : 0.8;
    }

    private async void PlayMoment(string name, string[] reactions)
    {
        _momentName.Text = name;
        _momentPill.Visibility = Visibility.Visible;
        _actor.Wake();
        for (var i = 0; i < reactions.Length; i++)
        {
            _actor.Play(reactions[i], D.ReduceMotion);
            await Task.Delay((int)(MascotActor.Length(reactions[i]) * 1000) + 100);
        }

        _momentPill.Visibility = Visibility.Collapsed;
    }
}
