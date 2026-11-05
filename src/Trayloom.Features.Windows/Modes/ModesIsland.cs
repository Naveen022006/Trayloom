// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Colours and small builders shared by the Modes views. The island is always dark; the light style takes the theme's brushes.</summary>
internal static class ModesLook
{
    private static readonly Dictionary<string, Color> Palette = new(StringComparer.Ordinal)
    {
        ["blue"] = Color.FromRgb(0x0A, 0x84, 0xFF),
        ["purple"] = Color.FromRgb(0xBF, 0x5A, 0xF2),
        ["pink"] = Color.FromRgb(0xFF, 0x37, 0x5F),
        ["red"] = Color.FromRgb(0xFF, 0x45, 0x3A),
        ["orange"] = Color.FromRgb(0xFF, 0x9F, 0x0A),
        ["yellow"] = Color.FromRgb(0xFF, 0xD6, 0x0A),
        ["green"] = Color.FromRgb(0x30, 0xD1, 0x58),
        ["mint"] = Color.FromRgb(0x63, 0xE6, 0xE2),
        ["teal"] = Color.FromRgb(0x40, 0xC8, 0xE0),
        ["cyan"] = Color.FromRgb(0x64, 0xD2, 0xFF),
        ["indigo"] = Color.FromRgb(0x5E, 0x5C, 0xE6),
        ["graphite"] = Color.FromRgb(0x8E, 0x8E, 0x93),
    };

    private static readonly Dictionary<(uint, int), SolidColorBrush> Cache = [];

    /// <summary>The colour of a mode's colour name (blue when unknown).</summary>
    public static Color ColorOf(string? name) => name is not null && Palette.TryGetValue(name, out var color) ? color : Palette["blue"];

    /// <summary>A frozen brush for a colour at an opacity.</summary>
    public static Brush Of(Color color, double opacity = 1)
    {
        var key = ((uint)(color.R << 16 | color.G << 8 | color.B), (int)Math.Round(opacity * 1000));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush White(double opacity) => Of(Colors.White, opacity);

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? White(1),
            FontFamily = UiKit.TextFont,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = UiKit.IconFont,
        FontSize = size,
        Foreground = brush ?? White(0.6),
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    /// <summary>A button that UI Automation can find and invoke: a rounded fill, a hover tint and an optional ring in <paramref name="ring"/>.</summary>
    public static Button TileButton(UIElement content, Action onClick, string automationId, string name, bool island, double radius, Thickness padding, Brush? ring = null, bool pill = false)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, padding);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(pill ? 0 : 2));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        if (island)
        {
            border.SetValue(Border.BackgroundProperty, White(0.1));
        }
        else
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        }

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, island ? White(0.2) : UiKit.BrushOf(ThemeKeys.RowHover), "chrome"));
        template.Triggers.Add(hover);
        var button = new Button
        {
            Content = content,
            Template = template,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = ring ?? Brushes.Transparent,
            FocusVisualStyle = null,
        };
        button.Click += (_, _) => onClick();
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name);
        return button;
    }
}

/// <summary>
/// The grid of mode tiles shared by the island page (dark) and the Quick panel (light): three columns of 96 x 72 tiles, each a
/// coloured circle with the mode's icon and its name. The active tile has a ring and says "Active"; the tile being switched to says
/// "Opening…". It rebuilds itself when the runtime changes.
/// </summary>
internal sealed class ModeTilesView : Border
{
    public const int Columns = 3;
    public const double TileWidth = 96;
    public const double TileHeight = 72;
    private const double Gap = 8;

    private readonly ModesRuntime _runtime;
    private readonly bool _island;
    private readonly Action<string> _pick;
    private readonly Action _create;
    private readonly Action? _save;
    private readonly Action? _leave;
    private readonly Action? _resized;

    /// <param name="runtime">The Modes service.</param>
    /// <param name="island">True for the dark island style, false for the themed light style.</param>
    /// <param name="pick">Called with the mode id when a tile is clicked.</param>
    /// <param name="create">Called by the "Create a mode" button of the empty state.</param>
    /// <param name="save">When set, the footer shows "Save what’s open now…" and calls this.</param>
    /// <param name="leave">When set, the footer shows "Leave {mode}" while a mode is active and calls this.</param>
    /// <param name="resized">Called after a rebuild changed the size, so the host can re-measure.</param>
    public ModeTilesView(ModesRuntime runtime, bool island, Action<string> pick, Action create, Action? save = null, Action? leave = null, Action? resized = null)
    {
        _runtime = runtime;
        _island = island;
        _pick = pick;
        _create = create;
        _save = save;
        _leave = leave;
        _resized = resized;
        Child = Build();
        Loaded += (_, _) =>
        {
            runtime.Changed -= OnChanged;
            runtime.Changed += OnChanged;
        };
        Unloaded += (_, _) => runtime.Changed -= OnChanged;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        Child = Build();
        _resized?.Invoke();
    }));

    private Brush Primary(double opacity = 1) => _island ? ModesLook.White(opacity) : UiKit.BrushOf(ThemeKeys.TextPrimary);

    private Brush Secondary => _island ? ModesLook.White(0.6) : UiKit.BrushOf(ThemeKeys.TextSecondary);

    private FrameworkElement Build()
    {
        var modes = _runtime.Modes;
        if (modes.Count == 0)
        {
            return Empty();
        }

        var active = _runtime.Active?.Id;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Columns, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var mode in modes)
        {
            grid.Children.Add(Tile(mode, mode.Id == active, _runtime.IsSwitching && _runtime.SwitchingToId == mode.Id));
        }

        // Two rows are shown; more scroll.
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 2 * (TileHeight + Gap),
            Content = grid,
        };
        var root = new StackPanel();
        root.Children.Add(scroll);
        if (_save is not null || (_leave is not null && _runtime.Active is not null))
        {
            root.Children.Add(Footer());
        }

        return root;
    }

    private FrameworkElement Tile(Mode mode, bool active, bool opening)
    {
        var color = ModesLook.ColorOf(mode.Color);
        var circle = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = ModesLook.Of(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = ModesLook.Icon(mode.Glyph, 13, Brushes.White),
        };
        var name = ModesLook.Text(mode.Name, 12, FontWeights.Medium, Primary());
        name.TextAlignment = TextAlignment.Center;
        name.MaxWidth = TileWidth - 12;
        name.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(circle);
        stack.Children.Add(name);
        if (active || opening)
        {
            var caption = ModesLook.Text(opening ? ModesStrings.Opening : ModesStrings.Active, 9.5, null, Secondary);
            caption.TextAlignment = TextAlignment.Center;
            stack.Children.Add(caption);
        }

        var button = ModesLook.TileButton(stack, () => _pick(mode.Id), "Modes.Tile." + mode.Id, mode.Name, _island, 10, new Thickness(2, 3, 2, 3), active ? ModesLook.Of(color) : null);
        button.Width = TileWidth;
        button.Height = TileHeight;
        button.Margin = new Thickness(0, 0, Gap, Gap);
        return button;
    }

    private FrameworkElement Footer()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        if (_leave is not null && _runtime.Active is { } active)
        {
            row.Children.Add(Pill(ModesStrings.Leave(active.Name), "Modes.Leave", _leave));
        }

        if (_save is not null)
        {
            row.Children.Add(Pill(ModesStrings.SaveCurrent, "Modes.Save", _save));
        }

        return row;
    }

    private Button Pill(string text, string automationId, Action onClick)
    {
        var label = ModesLook.Text(text, 11.5, FontWeights.Medium, Primary(0.9));
        var pill = ModesLook.TileButton(label, onClick, automationId, text, _island, 12, new Thickness(12, 5, 12, 5), pill: true);
        pill.Margin = new Thickness(0, 0, 8, 0);
        return pill;
    }

    private FrameworkElement Empty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        var line = ModesLook.Text(ModesStrings.NoModes, 13, null, Primary(0.9));
        line.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(line);
        var button = ModesLook.TileButton(ModesLook.Text(ModesStrings.CreateMode, 12, FontWeights.Medium, Primary()), _create, "Modes.Create", ModesStrings.CreateMode, _island, 10, new Thickness(12, 5, 12, 5), pill: true);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(button);
        return panel;
    }
}

/// <summary>The "Modes" page of the expanded island (spec 08 section 2.6): the tile grid and its footer pills.</summary>
public sealed class ModesIslandSection : IIslandSection
{
    public string? FeatureId => ModesKeys.FeatureId;

    public int Order => 125;

    public string Id => ModesKeys.FeatureId;

    public string Title => ModesStrings.Title;

    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<ModesRuntime>() is not { } runtime)
        {
            var message = ModesLook.Text(ModesStrings.RefusedOff, 13, null, ModesLook.White(0.7));
            message.HorizontalAlignment = HorizontalAlignment.Center;
            message.Margin = new Thickness(0, 12, 0, 12);
            return message;
        }

        void OpenSettings() => context.Shell.OpenSettings("quick-tools#modes");
        return new ModeTilesView(
            runtime,
            island: true,
            pick: id => runtime.Enter(id, "tile"),
            create: OpenSettings,
            save: () =>
            {
                ModesRequests.RequestCapture();
                OpenSettings();
            },
            leave: () => runtime.Leave("tile"),
            resized: context.Invalidate);
    }
}

/// <summary>
/// The "modes.notice" strip: "{mode} is ready" or "{mode}: n didn’t open", shown once on the island when a switch finished and the
/// person did not click a tile (or something failed). Clicking it opens the Modes page.
/// </summary>
public sealed class ModesNoticeStrip : IIslandStrip
{
    public string? FeatureId => ModesKeys.FeatureId;

    public int Order => 46;

    public string Id => "modes.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<ModesRuntime>() is not { } runtime)
        {
            return null;
        }

        // Captured early so a finished switch can ask the island to redraw.
        runtime.IslandContext = context;
        if (runtime.TakeNotice() is not { } text)
        {
            return null;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = ModesLook.Icon("", 12, ModesLook.Of(Color.FromRgb(0x0A, 0x84, 0xFF)));
        icon.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(icon);
        var label = ModesLook.Text(text, 11, FontWeights.Medium, ModesLook.White(0.95));
        label.MaxWidth = 260;
        row.Children.Add(label);
        return row;
    }
}
