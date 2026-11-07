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
    public static Button TileButton(UIElement content, Action onClick, string automationId, string name, bool island, double radius, Thickness padding, Brush? ring = null, bool pill = false, Brush? fill = null, Brush? fillHover = null)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, padding);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(pill ? 0 : 2));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        if (fill is not null)
        {
            // A button with its own colour (a swatch, the accent Save button) instead of the usual translucent tile.
            border.SetValue(Border.BackgroundProperty, fill);
        }
        else if (island)
        {
            border.SetValue(Border.BackgroundProperty, White(0.1));
        }
        else
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        }

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        // Centred unless the caller sets HorizontalContentAlignment (a list row that fills the width and reads from the left).
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, fill is not null ? fillHover ?? fill : island ? White(0.2) : UiKit.BrushOf(ThemeKeys.RowHover), "chrome"));
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
    private readonly Action? _add;
    private readonly Action<string>? _edit;
    private string? _manageId;
    private bool _confirmDelete;

    /// <param name="runtime">The Modes service.</param>
    /// <param name="island">True for the dark island style, false for the themed light style.</param>
    /// <param name="pick">Called with the mode id when a tile is clicked.</param>
    /// <param name="create">Called by the "Create a mode" button of the empty state.</param>
    /// <param name="save">When set, the footer shows "Save what’s open now…" and calls this.</param>
    /// <param name="leave">When set, the footer shows "Leave {mode}" while a mode is active and calls this.</param>
    /// <param name="resized">Called after a rebuild changed the size, so the host can re-measure.</param>
    /// <param name="add">When set, a "New mode" tile ends the grid and calls this.</param>
    /// <param name="edit">When set, every tile has a "…" button (and a right click) that opens Edit, Move and Delete, and Edit calls this with the mode id.</param>
    public ModeTilesView(ModesRuntime runtime, bool island, Action<string> pick, Action create, Action? save = null, Action? leave = null, Action? resized = null, Action? add = null, Action<string>? edit = null)
    {
        _runtime = runtime;
        _island = island;
        _pick = pick;
        _create = create;
        _save = save;
        _leave = leave;
        _resized = resized;
        _add = add;
        _edit = edit;
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

        if (_add is not null && _runtime.CanAdd)
        {
            grid.Children.Add(NewTile());
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
        if (_edit is not null && modes.FirstOrDefault(m => m.Id == _manageId) is { } managed)
        {
            root.Children.Add(ManageStrip(managed, modes.Select(m => m.Id).ToList().IndexOf(managed.Id), modes.Count));
        }
        else if (_add is not null && !_runtime.CanAdd)
        {
            var limit = ModesLook.Text(ModesUiStrings.LimitReached, 11, null, Secondary);
            limit.Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(limit);
        }

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
        if (_edit is null)
        {
            button.Margin = new Thickness(0, 0, Gap, Gap);
            return button;
        }

        // The island page can manage a mode in place: a small "…" in the corner, or a right click, opens its actions under the grid.
        // The right click is handled here so the island's own menu does not also appear.
        button.MouseRightButtonUp += (_, e) =>
        {
            Manage(mode.Id);
            e.Handled = true;
        };
        var more = ModesLook.TileButton(ModesLook.Icon(Glyphs.More, 11, Primary(0.8)), () => Manage(mode.Id), "Modes.Island.More." + mode.Id, ModesUiStrings.ModeOptions, _island, 6, new Thickness(0), pill: true);
        more.Width = 20;
        more.Height = 20;
        more.HorizontalAlignment = HorizontalAlignment.Right;
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Margin = new Thickness(0, 3, 3, 0);
        more.ToolTip = ModesUiStrings.ModeOptions;
        var cell = new Grid { Width = TileWidth, Height = TileHeight, Margin = new Thickness(0, 0, Gap, Gap) };
        cell.Children.Add(button);
        cell.Children.Add(more);
        return cell;
    }

    private FrameworkElement NewTile()
    {
        var plus = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1.5),
            BorderBrush = ModesLook.White(0.45),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = ModesLook.Icon(Glyphs.Add, 12, ModesLook.White(0.8)),
        };
        var name = ModesLook.Text(ModesUiStrings.NewMode, 12, FontWeights.Medium, Primary(0.8));
        name.TextAlignment = TextAlignment.Center;
        name.MaxWidth = TileWidth - 12;
        name.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(plus);
        stack.Children.Add(name);
        var button = ModesLook.TileButton(stack, () => _add!(), "Modes.Island.New", ModesUiStrings.NewMode, _island, 10, new Thickness(2, 3, 2, 3));
        button.Width = TileWidth;
        button.Height = TileHeight;
        button.Margin = new Thickness(0, 0, Gap, Gap);
        return button;
    }

    /// <summary>Opens or closes the action strip of a mode (a second click on its "…" closes it).</summary>
    private void Manage(string modeId)
    {
        _manageId = _manageId == modeId ? null : modeId;
        _confirmDelete = false;
        Rebuild();
    }

    private void Rebuild()
    {
        Child = Build();
        _resized?.Invoke();
    }

    /// <summary>The actions of one mode: Edit, Move left, Move right and Delete (which asks first, in the same strip).</summary>
    private FrameworkElement ManageStrip(Mode mode, int index, int count)
    {
        var id = mode.Id;
        var strip = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        if (_confirmDelete)
        {
            var question = ModesLook.Text(ModesUiStrings.DeleteConfirm(mode.Name), 11.5, null, Primary(0.9));
            question.TextWrapping = TextWrapping.Wrap;
            question.Margin = new Thickness(0, 0, 0, 6);
            strip.Children.Add(question);
            var confirm = ManagePill(ModesUiStrings.Delete, "Modes.Island.DeleteConfirm." + id, () =>
            {
                _manageId = null;
                _confirmDelete = false;
                _runtime.Delete(id);
            }, ModesLook.Of(Color.FromRgb(0xFF, 0x45, 0x3A)));
            row.Children.Add(confirm);
            row.Children.Add(ManagePill(ModesUiStrings.Cancel, "Modes.Island.DeleteCancel." + id, () =>
            {
                _confirmDelete = false;
                Rebuild();
            }));
            strip.Children.Add(row);
            return strip;
        }

        row.Children.Add(ManagePill(ModesUiStrings.Edit, "Modes.Island.Edit." + id, () => _edit!(id)));
        var left = ManagePill(ModesUiStrings.MoveLeft, "Modes.Island.MoveLeft." + id, () => _runtime.Move(id, -1));
        left.IsEnabled = index > 0;
        left.Opacity = left.IsEnabled ? 1 : 0.4;
        row.Children.Add(left);
        var right = ManagePill(ModesUiStrings.MoveRight, "Modes.Island.MoveRight." + id, () => _runtime.Move(id, 1));
        right.IsEnabled = index >= 0 && index < count - 1;
        right.Opacity = right.IsEnabled ? 1 : 0.4;
        row.Children.Add(right);
        row.Children.Add(ManagePill(ModesUiStrings.Delete, "Modes.Island.Delete." + id, () =>
        {
            _confirmDelete = true;
            Rebuild();
        }, ModesLook.Of(Color.FromRgb(0xFF, 0x45, 0x3A))));
        strip.Children.Add(row);
        return strip;
    }

    private Button ManagePill(string text, string automationId, Action onClick, Brush? brush = null)
    {
        var label = ModesLook.Text(text, 11.5, FontWeights.Medium, brush ?? Primary(0.9));
        var pill = ModesLook.TileButton(label, onClick, automationId, text, _island, 12, new Thickness(12, 5, 12, 5), pill: true);
        pill.Margin = new Thickness(0, 0, 8, 6);
        return pill;
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
        var button = ModesLook.TileButton(ModesLook.Text(ModesStrings.CreateMode, 12, FontWeights.Medium, Primary()), _create, _island ? "Modes.Island.New" : "Modes.Create", ModesStrings.CreateMode, _island, 10, new Thickness(12, 5, 12, 5), pill: true);
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

        return new ModesIslandView(runtime, context);
    }
}

/// <summary>
/// What outlives a rebuild of the Modes island page: the form being filled in, and the hold that keeps the island open while it is. The
/// island rebuilds its page whenever a song changes, and the old view is dropped with its text boxes, so the values live here.
/// </summary>
internal static class ModesIslandState
{
    private static IDisposable? s_hold;
    private static int s_views;
    private static System.Windows.Threading.DispatcherTimer? s_timer;

    /// <summary>The mode being created or edited in the island, or null while the tiles show.</summary>
    public static ModesDraft? Draft { get; set; }

    /// <summary>A view of the page entered the tree: while a form is open the island is held open again (a rebuild or reopening drops the old hold).</summary>
    public static void Attach(IIslandContext context)
    {
        s_views++;
        s_timer?.Stop();
        if (Draft is not null)
        {
            s_hold ??= context.HoldOpen();
        }
    }

    /// <summary>
    /// A view of the page left the tree. A relayout or rebuild puts a new one back within the same moment, so the hold is only let go
    /// when no view returns for a short while (the island closed, or the person went to another page); otherwise the island would never
    /// collapse by itself again.
    /// </summary>
    public static void Detach(IIslandContext context)
    {
        s_views = Math.Max(0, s_views - 1);
        if (s_views > 0 || s_hold is null)
        {
            return;
        }

        if (s_timer is null)
        {
            s_timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            s_timer.Tick += OnTimer;
        }

        s_timer.Stop();
        s_timer.Start();
    }

    private static void OnTimer(object? sender, EventArgs e)
    {
        s_timer?.Stop();
        if (s_views == 0)
        {
            Release();
        }
    }

    /// <summary>Holds the island open for as long as the form is up.</summary>
    public static void Hold(IIslandContext context) => s_hold ??= context.HoldOpen();

    /// <summary>Lets the island collapse by itself again.</summary>
    public static void Release()
    {
        s_hold?.Dispose();
        s_hold = null;
    }
}

/// <summary>
/// The content of the Modes island page: the tile grid, or the inline form for a new or edited mode. It tells the island (through
/// <see cref="IIslandLayerHost"/>) that Esc and Back close the form first, and asks it to measure the page again whenever it swaps.
/// </summary>
internal sealed class ModesIslandView : Border, IIslandLayerHost
{
    private readonly ModesRuntime _runtime;
    private readonly IIslandContext _context;
    private ModesIslandEditor? _editor;
    private double _lastHeight;
    private bool _relayoutPending;
    private bool _suggestionShown;

    public ModesIslandView(ModesRuntime runtime, IIslandContext context)
    {
        _runtime = runtime;
        _context = context;
        Show();
        Loaded += (_, _) =>
        {
            ModesIslandState.Attach(_context);
            _runtime.Changed -= OnRuntimeChanged;
            _runtime.Changed += OnRuntimeChanged;
        };
        Unloaded += (_, _) =>
        {
            _runtime.Changed -= OnRuntimeChanged;
            ModesIslandState.Detach(_context);
        };
    }

    /// <summary>The question of an automatic start appears or goes while the page is open: the page swaps once to add or remove its card.</summary>
    private void OnRuntimeChanged()
    {
        if (ModesIslandState.Draft is null && (_runtime.PendingSuggestion is not null) != _suggestionShown)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (ModesIslandState.Draft is null)
                {
                    Show();
                    Relayout();
                }
            });
        }
    }

    /// <summary>The "{app} opened · Start {mode}?" card with its two answers; null when there is no open question.</summary>
    private FrameworkElement? SuggestionCard()
    {
        if (_runtime.PendingSuggestion is not { } suggestion || _runtime.Modes.All(m => m.Id != suggestion.ModeId))
        {
            return null;
        }

        var modeName = _runtime.Modes.First(m => m.Id == suggestion.ModeId).Name;
        var text = ModesLook.Text(suggestion.Text, 12, FontWeights.Medium, ModesLook.White(0.95));
        text.TextWrapping = TextWrapping.Wrap;
        var yes = ModesLook.TileButton(ModesLook.Text(ModesAutoStrings.StartMode(modeName), 11.5, FontWeights.SemiBold, ModesLook.White(1)), () => _runtime.AcceptSuggestion(), "Modes.Suggestion.Start", ModesAutoStrings.StartMode(modeName), true, 12, new Thickness(12, 5, 12, 5), pill: true, fill: ModesLook.Of(Color.FromRgb(0x0A, 0x84, 0xFF)), fillHover: ModesLook.Of(Color.FromRgb(0x3B, 0x9B, 0xFF)));
        var no = ModesLook.TileButton(ModesLook.Text(ModesAutoStrings.NotNow, 11.5, FontWeights.Medium, ModesLook.White(0.9)), () => _runtime.DismissSuggestion(), "Modes.Suggestion.NotNow", ModesAutoStrings.NotNow, true, 12, new Thickness(12, 5, 12, 5), pill: true);
        no.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var inner = new StackPanel();
        inner.Children.Add(text);
        inner.Children.Add(buttons);
        return new Border
        {
            Child = inner,
            CornerRadius = new CornerRadius(10),
            Background = ModesLook.White(0.08),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    public bool CanStepBack => ModesIslandState.Draft is not null;

    /// <summary>Closes the open layer inside the form first (the app list, the web box), then the form itself without saving.</summary>
    public bool StepBack()
    {
        if (ModesIslandState.Draft is null)
        {
            return false;
        }

        if (_editor?.StepBack() != true)
        {
            CloseForm();
        }

        return true;
    }

    private void Show()
    {
        _editor = null;
        if (ModesIslandState.Draft is { } draft)
        {
            _editor = new ModesIslandEditor(_runtime, _context, draft, CloseForm, Relayout);
            Child = _editor;
            return;
        }

        var tiles = new ModeTilesView(
            _runtime,
            island: true,
            pick: id => _runtime.Enter(id, "tile"),
            create: () => StartNew(false),
            save: () => StartNew(true),
            leave: () => _runtime.Leave("tile"),
            resized: TilesResized,
            add: () => StartNew(false),
            edit: StartEdit);

        // An automatic start that wants to know first puts its question above the tiles.
        var card = SuggestionCard();
        _suggestionShown = card is not null;
        if (card is null)
        {
            Child = tiles;
            return;
        }

        var page = new StackPanel();
        page.Children.Add(card);
        page.Children.Add(tiles);
        Child = page;
    }

    /// <summary>A rebuilt tile grid asks for a new measure only when its height really changed (a switch rebuilds it often).</summary>
    private void TilesResized()
    {
        Measure(new Size(Math.Max(1, ActualWidth), double.PositiveInfinity));
        if (Math.Abs(DesiredSize.Height - _lastHeight) > 0.5)
        {
            _lastHeight = DesiredSize.Height;
            Relayout();
        }
    }

    private void StartNew(bool useOpenNow)
    {
        if (!_runtime.CanAdd)
        {
            return;
        }

        ModesIslandState.Draft = ModesDraft.ForNew();
        Open(useOpenNow);
    }

    private void StartEdit(string id)
    {
        if (_runtime.Modes.FirstOrDefault(m => m.Id == id) is not { } mode)
        {
            return;
        }

        ModesIslandState.Draft = ModesDraft.ForEdit(mode);
        Open(false);
    }

    private void Open(bool useOpenNow)
    {
        ModesIslandState.Hold(_context);
        Show();
        Relayout();
        _editor?.Begin(useOpenNow);
    }

    /// <summary>Back to the tiles, with nothing saved (Save already stored the mode before calling this).</summary>
    private void CloseForm()
    {
        ModesIslandState.Draft = null;
        ModesIslandState.Release();
        _context.ReleaseKeyboard();
        Show();
        Relayout();
    }

    /// <summary>
    /// Asks the island to measure the page again after the content changed size. The island moves the view into a fresh layout for that,
    /// which drops the keyboard focus, so a text box that had it gets it back.
    /// </summary>
    private void Relayout()
    {
        if (_relayoutPending)
        {
            return;
        }

        _relayoutPending = true;
        var focused = Keyboard.FocusedElement as TextBox;
        var caret = focused?.CaretIndex ?? 0;
        if (focused is not null && !focused.IsDescendantOf(this))
        {
            focused = null;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _relayoutPending = false;
            _context.Relayout();
            if (focused is not null)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (focused.IsLoaded)
                    {
                        _context.EnsureKeyboard();
                        Keyboard.Focus(focused);
                        focused.CaretIndex = Math.Min(caret, focused.Text.Length);
                    }
                });
            }
        });
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
