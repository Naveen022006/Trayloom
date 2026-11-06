// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The "New mode" / "Edit" form inside the Dynamic Island (spec 08 section 2.6): name, colour and icon, the items a mode opens (from what
/// is open now, or one by one), and a folded "More options". It edits a <see cref="ModesDraft"/> that lives outside the view, because the
/// island rebuilds its page whenever a song changes. Nothing reaches <see cref="ModesRuntime"/> until Save, and the Settings editor stays
/// the advanced path ("More settings…"). The island is always dark, so the controls are drawn here in the island's own look.
/// </summary>
internal sealed class ModesIslandEditor : Grid
{
    private const string Id = "Modes.Island.Form.";

    private static readonly Color AccentColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color OrangeColor = Color.FromRgb(0xFF, 0x9F, 0x0A);

    private static readonly string[] LeaveValues = ["keep", "minimize", "close"];

    private readonly ModesRuntime _runtime;
    private readonly IIslandContext _context;
    private readonly ModesDraft _draft;
    private readonly Action _close;
    private readonly Action _relayout;

    private readonly TextBox _name;
    private readonly TextBlock _nameNote = Note();
    private readonly WrapPanel _colours = new();
    private readonly WrapPanel _icons = new();
    private readonly StackPanel _captureHost = new();
    private readonly StackPanel _itemsHost = new();
    private readonly TextBlock _itemsNote = Note();
    private readonly List<Button> _addButtons = [];
    private readonly StackPanel _panelHost = new();
    private readonly StackPanel _moreHost = new();
    private readonly Button _save;
    private readonly TextBlock _saveNote = Note();

    // The inline panels
    private readonly StackPanel _webChips = new();
    private readonly TextBlock _webNote = Note();
    private TextBox? _webBox;
    private TextBlock? _tickNote;
    private Button? _addTicked;
    private IReadOnlyList<BrowserChoice>? _browsers;
    private IReadOnlyList<AppEntry>? _apps;
    private bool _appsLoading;
    private bool _readingTabs;
    private bool _touched;

    /// <param name="runtime">The Modes service.</param>
    /// <param name="context">The island context: keyboard, hold and the Settings link.</param>
    /// <param name="draft">The mode being filled in (kept by <see cref="ModesIslandState"/>).</param>
    /// <param name="close">Called when the form is done (saved or cancelled) and the tiles should show again.</param>
    /// <param name="relayout">Called after the form changed size, so the island measures the page again.</param>
    public ModesIslandEditor(ModesRuntime runtime, IIslandContext context, ModesDraft draft, Action close, Action relayout)
    {
        _runtime = runtime;
        _context = context;
        _draft = draft;
        _close = close;
        _relayout = relayout;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Children.Add(BuildHeader());

        var body = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(Label(ModesUiStrings.Name, 6));
        var field = Field(out _name, _draft.Mode.Name, ModesUiStrings.IslandNameHint, multiline: false);
        _name.MaxLength = ModesMath.MaxNameLength;
        AutomationProperties.SetAutomationId(_name, Id + "Name");
        AutomationProperties.SetName(_name, ModesUiStrings.Name);
        _name.PreviewMouseLeftButtonDown += (_, _) => _context.EnsureKeyboard();
        _name.TextChanged += (_, _) =>
        {
            _draft.Mode.Name = _name.Text;
            _touched = true;
            UpdateValidity();
        };
        _name.PreviewKeyDown += OnNameKey;
        body.Children.Add(field);
        body.Children.Add(_nameNote);

        body.Children.Add(Label(ModesUiStrings.Colour));
        body.Children.Add(_colours);
        body.Children.Add(Label(ModesUiStrings.Icon, 6));
        body.Children.Add(_icons);

        var useOpen = Pill(ModesUiStrings.UseOpenNow, Id + "UseOpen", UseOpenNow, accent: true);
        useOpen.HorizontalAlignment = HorizontalAlignment.Left;
        useOpen.Margin = new Thickness(0, 14, 0, 6);
        body.Children.Add(useOpen);
        body.Children.Add(_captureHost);

        body.Children.Add(Label(ModesUiStrings.WhatOpens));
        body.Children.Add(_itemsHost);
        body.Children.Add(_itemsNote);
        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        AddButton(add, ModesUiStrings.IslandApp, Id + "AddApp", () => TogglePanel("app"));
        AddButton(add, ModesUiStrings.IslandWeb, Id + "AddWeb", () => TogglePanel("web"));
        AddButton(add, ModesUiStrings.IslandFolder, Id + "AddFolder", AddFolder);
        AddButton(add, ModesUiStrings.IslandFile, Id + "AddFile", AddFile);
        body.Children.Add(add);
        body.Children.Add(_panelHost);

        body.Children.Add(_moreHost);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
        SetRow(scroll, 1);
        Children.Add(scroll);

        _save = Pill(ModesUiStrings.Save, Id + "Save", Commit, accent: true);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 10, 0) };
        footer.Children.Add(_saveNote);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Pill(ModesUiStrings.Cancel, Id + "Cancel", _close);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        footer.Children.Add(buttons);
        SetRow(footer, 2);
        Children.Add(footer);

        BuildLook();
        BuildCapture();
        BuildItems();
        BuildPanel();
        BuildMore();
        UpdateValidity();

        Loaded += (_, _) =>
        {
            _draft.CaptureChanged -= OnCaptureChanged;
            _draft.CaptureChanged += OnCaptureChanged;
        };
        Unloaded += (_, _) => _draft.CaptureChanged -= OnCaptureChanged;
    }

    // ------------------------------------------------------------------------------------------ entry and exit

    /// <summary>Called once, after the island measured the page for the form: reads what is open (when asked) or puts the caret in the name box.</summary>
    public void Begin(bool useOpenNow)
    {
        if (useOpenNow)
        {
            UseOpenNow();
        }
        else if (_draft.IsNew)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusName);
        }
    }

    /// <summary>Esc: closes the inline list, web box or captured list that is open; false when there is none (the form then cancels).</summary>
    public bool StepBack()
    {
        if (_draft.Panel is not null)
        {
            _draft.Panel = null;
            BuildPanel();
            _relayout();
            return true;
        }

        if (_draft.Captured is not null)
        {
            _draft.ClearCapture();
            BuildCapture();
            _relayout();
            return true;
        }

        return false;
    }

    private void FocusName()
    {
        if (!IsLoaded)
        {
            return;
        }

        _context.EnsureKeyboard();
        _name.Focus();
        Keyboard.Focus(_name);
        _name.CaretIndex = _name.Text.Length;
    }

    /// <summary>Enter in the name box saves when the form is valid; Esc is the island's own step-back, which cancels the form.</summary>
    private void OnNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = ModesLook.Text(_draft.IsNew ? ModesUiStrings.EditorNew : ModesUiStrings.EditorEdit, 15, FontWeights.SemiBold);
        header.Children.Add(title);
        var settings = ModesLook.TileButton(ModesLook.Text(ModesUiStrings.MoreSettings, 11.5, FontWeights.Medium, ModesLook.Of(AccentColor)), () => _context.Shell.OpenSettings("quick-tools#modes"), Id + "MoreSettings", ModesUiStrings.MoreSettings, true, 12, new Thickness(10, 4, 10, 4), pill: true, fill: Brushes.Transparent, fillHover: ModesLook.White(0.1));
        SetColumn(settings, 1);
        header.Children.Add(settings);
        return header;
    }

    // ------------------------------------------------------------------------------------------ validation and save

    private void UpdateValidity()
    {
        var problem = _draft.Problem(_runtime.Modes, _runtime.CanAdd);

        // The empty-name message waits until the person has typed or pressed Save, so a fresh form does not open with a complaint.
        var quiet = !_touched && problem == DraftProblem.NameMissing;
        ModesDialogKit.Say(_nameNote, quiet ? null : problem switch
        {
            DraftProblem.NameMissing => ModesUiStrings.NameMissing,
            DraftProblem.NameTaken => ModesUiStrings.NameTaken,
            _ => null,
        });
        ModesDialogKit.Say(_saveNote, problem == DraftProblem.LimitReached ? ModesUiStrings.LimitReached : null);
        _save.IsEnabled = problem == DraftProblem.None;
        _save.Opacity = _save.IsEnabled ? 1 : 0.4;
    }

    private void Commit()
    {
        _touched = true;
        UpdateValidity();
        if (!_save.IsEnabled)
        {
            return;
        }

        var mode = _draft.ToMode();
        if (_draft.IsNew)
        {
            _runtime.Add(mode);
        }
        else
        {
            _runtime.Update(mode);
        }

        _close();
    }

    // ------------------------------------------------------------------------------------------ colour and icon

    /// <summary>The swatches and glyphs, with a ring on the colour in use and the icon filled with it. Small enough to share one row each.</summary>
    private void BuildLook()
    {
        _colours.Children.Clear();
        foreach (var name in ModesMath.Colors)
        {
            var selected = name == _draft.Mode.Color;
            var colour = name;
            var button = ModesLook.TileButton(new Grid(), () => Pick(() => _draft.Mode.Color = colour), Id + "Color." + name, name, true, 12, new Thickness(0), selected ? ModesLook.White(1) : null, fill: ModesLook.Of(ModesLook.ColorOf(name)));
            button.Width = 26;
            button.Height = 26;
            button.Margin = new Thickness(0, 0, 6, 6);
            _colours.Children.Add(button);
        }

        _icons.Children.Clear();
        var current = ModesLook.Of(ModesLook.ColorOf(_draft.Mode.Color));
        var number = 0;
        foreach (var glyph in ModesMath.Glyphs)
        {
            number++;
            var chosen = glyph;
            var selected = glyph == _draft.Mode.Glyph;
            var button = ModesLook.TileButton(ModesLook.Icon(glyph, 13, selected ? Brushes.White : ModesLook.White(0.85)), () => Pick(() => _draft.Mode.Glyph = chosen), Id + "Icon." + number, ModesUiStrings.Icon + " " + number, true, 8, new Thickness(0), null, fill: selected ? current : null);
            button.Width = 28;
            button.Height = 28;
            button.Margin = new Thickness(0, 0, 6, 6);
            _icons.Children.Add(button);
        }
    }

    private void Pick(System.Action change)
    {
        change();
        BuildLook();
    }

    // ------------------------------------------------------------------------------------------ what is open now

    /// <summary>Reads what is open (on a worker thread) and lists it, ticked, under the button. Nothing is saved or added until "Add the ticked items".</summary>
    private async void UseOpenNow()
    {
        if (_draft.Capturing)
        {
            return;
        }

        _draft.Capturing = true;
        _draft.CaptureNote = null;
        _draft.ClearCapture();
        BuildCapture();
        _relayout();
        try
        {
            var capture = await _runtime.CaptureAsync();
            _draft.SetCapture(capture);
            _draft.CaptureNote = capture.Items.Count == 0 ? ModesUiStrings.CaptureNothing : null;
        }
        catch (Exception ex)
        {
            _context.Log.Info($"[modes] capture failed: {ex.GetType().Name}");
            _draft.CaptureNote = ModesUiStrings.CaptureFailed;
        }
        finally
        {
            _draft.Capturing = false;
        }

        // Raised on the draft, not on this view: the page may have been rebuilt while the read ran, and the view on screen shows the result.
        _draft.RaiseCaptureChanged();
    }

    private void OnCaptureChanged()
    {
        BuildCapture();
        _relayout();
    }

    private void BuildCapture()
    {
        _captureHost.Children.Clear();
        _tickNote = null;
        _addTicked = null;
        if (_draft.Capturing)
        {
            _captureHost.Children.Add(Caption(ModesUiStrings.CaptureReading));
            return;
        }

        if (_draft.CaptureNote is { } note)
        {
            _captureHost.Children.Add(Caption(note));
        }

        if (_draft.Captured is not { Items.Count: > 0 } capture)
        {
            return;
        }

        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 8), Background = ModesLook.White(0.06), Margin = new Thickness(0, 2, 0, 0) };
        var list = new StackPanel();
        AddGroup(list, ModesUiStrings.CaptureApps, capture, ModeItemKind.App, "Apps");
        AddGroup(list, ModesUiStrings.CaptureBrowserWindows, capture, ModeItemKind.Web, "Browsers");
        AddGroup(list, ModesUiStrings.CaptureFolders, capture, ModeItemKind.Path, "Folders");
        foreach (var warning in capture.Warnings)
        {
            var line = Wrapped(warning, 11, ModesLook.Of(OrangeColor));
            line.Margin = new Thickness(0, 6, 0, 0);
            list.Children.Add(line);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _addTicked = Pill(ModesUiStrings.AddTickedItems, Id + "AddTicked", AddTicked, accent: true);
        _addTicked.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_addTicked);
        buttons.Children.Add(Pill(ModesUiStrings.Close, Id + "CaptureClose", () =>
        {
            _draft.ClearCapture();
            BuildCapture();
            _relayout();
        }));
        list.Children.Add(buttons);
        _tickNote = Note();
        list.Children.Add(_tickNote);
        card.Child = list;
        _captureHost.Children.Add(card);
        UpdateTicks();
    }

    private void AddGroup(StackPanel list, string title, ModeCapture capture, ModeItemKind kind, string idPart)
    {
        var number = 0;
        for (var i = 0; i < capture.Items.Count; i++)
        {
            if (capture.Items[i].Item.Kind != kind)
            {
                continue;
            }

            if (number == 0)
            {
                list.Children.Add(Label(title, 8));
            }

            number++;
            list.Children.Add(CaptureRow(capture.Items[i], i, Id + "Captured." + idPart + "." + number));
        }
    }

    /// <summary>One captured thing: a tick, its icon, its name and a short detail (a browser window says how many tabs it has).</summary>
    private FrameworkElement CaptureRow(CapturedItem captured, int index, string id)
    {
        var item = captured.Item;
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(ItemIcon(item, 26));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ModesLook.Text(item.Name, 12.5, FontWeights.Medium));
        if (captured.Detail.Length > 0)
        {
            text.Children.Add(ModesLook.Text(captured.Detail, 11, null, ModesLook.White(0.6)));
        }

        SetColumn(text, 2);
        line.Children.Add(text);

        var box = DarkCheck(!_draft.Unticked.Contains(index), line, id, item.Name);
        box.Margin = new Thickness(0, 3, 0, 3);
        box.Click += (_, _) =>
        {
            _draft.Tick(index, box.IsChecked == true);
            UpdateTicks();
        };
        return box;
    }

    /// <summary>"Add the ticked items" needs at least one tick, like the capture dialog's Save.</summary>
    private void UpdateTicks()
    {
        if (_addTicked is null || _tickNote is null)
        {
            return;
        }

        var none = _draft.TickedCount == 0;
        _addTicked.IsEnabled = !none;
        _addTicked.Opacity = none ? 0.4 : 1;
        ModesDialogKit.Say(_tickNote, none ? ModesUiStrings.CaptureNothingTicked : null);
    }

    private void AddTicked()
    {
        var result = _draft.AddTicked();
        ModesDialogKit.Say(_itemsNote, result.HitLimit ? ModesUiStrings.ItemLimit : null);
        BuildCapture();
        BuildItems();
        _relayout();
    }

    // ------------------------------------------------------------------------------------------ items

    private void BuildItems()
    {
        _itemsHost.Children.Clear();
        _draft.EnsureFront();
        var items = _draft.Mode.Items;
        if (items.Count == 0)
        {
            _itemsHost.Children.Add(Caption(ModesUiStrings.NothingYet));
        }

        for (var i = 0; i < items.Count; i++)
        {
            _itemsHost.Children.Add(ItemRow(items[i], i + 1));
        }

        var full = !_draft.CanAddItem;
        foreach (var button in _addButtons)
        {
            button.IsEnabled = !full;
            button.Opacity = full ? 0.4 : 1;
        }

        if (full)
        {
            ModesDialogKit.Say(_itemsNote, ModesUiStrings.ItemLimit);
        }
    }

    private FrameworkElement ItemRow(ModeItem item, int number)
    {
        var detail = DetailOf(item);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(ItemIcon(item, 28));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = detail };
        text.Children.Add(ModesLook.Text(item.Name, 12.5, FontWeights.Medium));
        text.Children.Add(ModesLook.Text(detail, 11, null, ModesLook.White(0.6)));
        SetColumn(text, 2);
        row.Children.Add(text);

        var remove = ModesLook.TileButton(ModesLook.Icon(Glyphs.Close, 10, ModesLook.White(0.8)), () =>
        {
            _draft.Remove(item.Id);
            ModesDialogKit.Say(_itemsNote, null);
            BuildItems();
            _relayout();
        }, Id + "Item." + number + ".Remove", ModesUiStrings.Remove, true, 8, new Thickness(0), pill: true);
        remove.Width = 26;
        remove.Height = 26;
        remove.ToolTip = ModesUiStrings.Remove;
        SetColumn(remove, 3);
        row.Children.Add(remove);

        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6), Background = ModesLook.White(0.08), Child = row };
        return ModesLandmark.Wrap(card, Id + "Item." + number, item.Name);
    }

    private IReadOnlyList<BrowserChoice> Browsers() => _browsers ??= _runtime.Browsers();

    private string BrowserName(string id) =>
        Browsers().FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase))?.Name
        ?? (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase) ? ModesStrings.DefaultBrowser : Path.GetFileNameWithoutExtension(id));

    private string DetailOf(ModeItem item) => item.Kind switch
    {
        ModeItemKind.Web => ModesUiStrings.Address(item.Urls.Count) + ModesUiStrings.Dot + BrowserName(item.Browser),
        ModeItemKind.Path => item.Path ?? string.Empty,
        _ => string.IsNullOrEmpty(item.Exe) ? item.Aumid ?? string.Empty : Path.GetFileName(item.Exe),
    };

    /// <summary>The application's own icon for an app, else a glyph for the kind of item on a soft tile.</summary>
    private static FrameworkElement ItemIcon(ModeItem item, double size)
    {
        if (item.Kind == ModeItemKind.App && !string.IsNullOrEmpty(item.Exe) && AppIdentity.IconOf(item.Exe) is { } icon)
        {
            var image = new Image { Source = icon, Width = size - 4, Height = size - 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            frame.Children.Add(image);
            return frame;
        }

        var glyph = item.Kind switch
        {
            ModeItemKind.Web => "",
            ModeItemKind.Path when ModesDialogKit.IsFolder(item.Path) => "",
            ModeItemKind.Path => "",
            _ => "",
        };
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(8),
            Background = ModesLook.White(0.12),
            VerticalAlignment = VerticalAlignment.Center,
            Child = ModesLook.Icon(glyph, 13, ModesLook.White(0.8)),
        };
    }

    /// <summary>Adds items (at most twenty in all, no repeats) and says what was left out.</summary>
    private void AddItems(IEnumerable<ModeItem> items)
    {
        var result = _draft.AddItems(items);
        ModesDialogKit.Say(_itemsNote, result.HitLimit ? ModesUiStrings.ItemLimit : null);
        BuildItems();
        _relayout();
    }

    private void AddButton(Panel host, string text, string id, System.Action click)
    {
        var button = Pill(text, id, click);
        button.Margin = new Thickness(0, 0, 8, 6);
        _addButtons.Add(button);
        host.Children.Add(button);
    }

    // ------------------------------------------------------------------------------------------ the add panels

    private void TogglePanel(string panel)
    {
        _draft.Panel = _draft.Panel == panel ? null : panel;
        BuildPanel();
        _relayout();
    }

    private void BuildPanel()
    {
        _panelHost.Children.Clear();
        _webBox = null;
        if (_draft.Panel == "app")
        {
            _panelHost.Children.Add(BuildAppPanel());
            if (_apps is null)
            {
                LoadApps();
            }
        }
        else if (_draft.Panel == "web")
        {
            _panelHost.Children.Add(BuildWebPanel());
        }
    }

    private static Border PanelCard(UIElement content) =>
        new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 2, 0, 6), Background = ModesLook.White(0.06), Child = content };

    // -- apps

    /// <summary>Running apps, listed here; every other app (installed ones, a program file) is one click away in the existing picker.</summary>
    private FrameworkElement BuildAppPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(ModesUiStrings.RunningApps, 0));
        if (_apps is null)
        {
            panel.Children.Add(Caption("…"));
        }
        else if (_apps.Count == 0)
        {
            panel.Children.Add(Caption(ModesUiStrings.NoRunningApps));
        }
        else
        {
            var list = new StackPanel();
            var number = 0;
            foreach (var entry in _apps)
            {
                number++;
                list.Children.Add(AppRow(entry, number));
            }

            panel.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 180,
                Content = list,
            });
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var more = Pill(ModesUiStrings.MoreApps, Id + "MoreApps", MoreApps);
        more.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(more);
        buttons.Children.Add(Pill(ModesUiStrings.Done, Id + "AppDone", () => TogglePanel("app")));
        panel.Children.Add(buttons);
        return PanelCard(panel);
    }

    private Button AppRow(AppEntry entry, int number)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (entry.Icon is not null)
        {
            var image = new Image { Source = entry.Icon, Width = 22, Height = 22 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
        }

        var name = ModesLook.Text(entry.Name, 12.5, FontWeights.Medium);
        SetColumn(name, 2);
        grid.Children.Add(name);
        var button = ModesLook.TileButton(grid, () => AddApp(entry.Identity), Id + "App." + number, entry.Name, true, 8, new Thickness(8, 5, 8, 5), pill: true, fill: Brushes.Transparent, fillHover: ModesLook.White(0.12));
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.ToolTip = entry.Identity;
        return button;
    }

    private async void LoadApps()
    {
        if (_appsLoading)
        {
            return;
        }

        _appsLoading = true;
        try
        {
            _apps = await Task.Run(AppPickerWindow.LoadRunning);
        }
        catch (Exception ex)
        {
            _context.Log.Info($"[modes] listing running apps failed: {ex.GetType().Name}");
            _apps = [];
        }
        finally
        {
            _appsLoading = false;
        }

        if (_draft.Panel == "app" && IsLoaded)
        {
            BuildPanel();
            _relayout();
        }
    }

    private void AddApp(string exe)
    {
        AddItems([new ModeItem { Kind = ModeItemKind.App, Name = AppIdentity.DisplayName(exe), Exe = exe.ToLowerInvariant() }]);
    }

    /// <summary>The existing picker (installed apps, Browse…), over the island and with the island held open meanwhile.</summary>
    private void MoreApps()
    {
        using var hold = _context.HoldOpen();
        var picker = new AppPickerWindow(null, [], allowBrowse: true);
        if (ModesDialogKit.ShowOver(picker, this) && picker.Result is { Length: > 0 } exe)
        {
            AddApp(exe);
        }
    }

    // -- web pages

    private FrameworkElement BuildWebPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(ModesUiStrings.Browser, 0));
        panel.Children.Add(_webChips);
        FillWebChips();

        var hint = Caption(ModesUiStrings.PasteAddresses);
        hint.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(hint);
        panel.Children.Add(Field(out var box, _draft.WebText, ModesUiStrings.PasteAddresses, multiline: true));
        _webBox = box;
        AutomationProperties.SetAutomationId(box, Id + "Web.Addresses");
        AutomationProperties.SetName(box, ModesUiStrings.PasteAddresses);
        box.PreviewMouseLeftButtonDown += (_, _) => _context.EnsureKeyboard();
        box.TextChanged += (_, _) => _draft.WebText = box.Text;

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        AddRight(buttons, Pill(ModesUiStrings.AddAddresses, Id + "Web.Add", AddPasted, accent: true));
        AddRight(buttons, Pill(ModesUiStrings.AddTabsNow, Id + "Web.Tabs", ReadTabs));
        buttons.Children.Add(Pill(ModesUiStrings.Done, Id + "Web.Done", () => TogglePanel("web")));
        panel.Children.Add(buttons);
        panel.Children.Add(_webNote);
        panel.Children.Add(Caption(ModesUiStrings.TabsCaption));
        return PanelCard(panel);
    }

    private static void AddRight(Panel host, Button button)
    {
        button.Margin = new Thickness(0, 0, 8, 6);
        host.Children.Add(button);
    }

    private BrowserChoice? SelectedBrowser() =>
        Browsers().FirstOrDefault(b => string.Equals(b.Id, _draft.BrowserId, StringComparison.OrdinalIgnoreCase)) ?? Browsers().FirstOrDefault();

    /// <summary>The browser choices and, for a Chromium browser with several profiles, the profile choices. Rebuilt on a click without touching the address box.</summary>
    private void FillWebChips()
    {
        _webChips.Children.Clear();
        var selected = SelectedBrowser();
        var browsers = new WrapPanel();
        var number = 0;
        foreach (var choice in Browsers())
        {
            number++;
            var id = choice.Id;
            browsers.Children.Add(Chip(choice.Name, ReferenceEquals(choice, selected), () =>
            {
                _draft.BrowserId = id;
                _draft.Profile = string.Empty;
                FillWebChips();
                _relayout();
            }, Id + "Web.Browser." + number));
        }

        _webChips.Children.Add(browsers);
        var profiles = selected is { Chromium: true } ? _runtime.ChromiumProfiles(selected.Id) : [];
        if (profiles.Count == 0)
        {
            return;
        }

        _webChips.Children.Add(Label(ModesUiStrings.Profile, 4));
        var options = new List<(string Dir, string Name)> { (string.Empty, ModesUiStrings.BrowserOwnChoice) };
        options.AddRange(profiles);
        var row = new WrapPanel();
        var profileNumber = 0;
        foreach (var (dir, name) in options)
        {
            profileNumber++;
            var chosen = dir;
            row.Children.Add(Chip(name, _draft.Profile == dir, () =>
            {
                _draft.Profile = chosen;
                FillWebChips();
            }, Id + "Web.Profile." + profileNumber));
        }

        _webChips.Children.Add(row);
    }

    private void AddPasted()
    {
        var item = _draft.PastedWebItem(_draft.WebText, out var rejected);
        if (item is null)
        {
            ModesDialogKit.Say(_webNote, rejected > 0 ? ModesUiStrings.Rejected(rejected) : ModesUiStrings.NoValidAddresses);
            return;
        }

        ModesDialogKit.Say(_webNote, rejected > 0 ? ModesUiStrings.Rejected(rejected) : null);
        _draft.WebText = string.Empty;
        if (_webBox is not null)
        {
            _webBox.Text = string.Empty;
        }

        AddItems([item]);
    }

    /// <summary>Reads the open tabs of the chosen Chrome or Edge profile (on this click only) and adds one web item per browser window.</summary>
    private async void ReadTabs()
    {
        if (_readingTabs || SelectedBrowser() is not { } browser)
        {
            return;
        }

        _readingTabs = true;
        try
        {
            ModesDialogKit.Say(_webNote, ModesUiStrings.ReadingTabs);
            var result = await _runtime.ReadBrowserTabsAsync(browser.Id, _draft.Profile);
            if (ModesIslandState.Draft != _draft)
            {
                return;
            }

            if (!result.Ok)
            {
                ModesDialogKit.Say(_webNote, result.Problem ?? ModesStrings.TabsUnreadable(browser.Name));
                return;
            }

            if (result.Windows.Count == 0)
            {
                ModesDialogKit.Say(_webNote, ModesUiStrings.NoTabsFound);
                return;
            }

            ModesDialogKit.Say(_webNote, null);
            var windows = result.Windows.Select((urls, index) => (index + 1, urls)).ToList();
            AddItems(_draft.TabWindowItems(browser.Name, windows));
        }
        catch (Exception ex)
        {
            _context.Log.Info($"[modes] reading tabs failed: {ex.GetType().Name}");
            ModesDialogKit.Say(_webNote, ModesStrings.TabsUnreadable(browser.Name));
        }
        finally
        {
            _readingTabs = false;
        }
    }

    // -- folders and files

    /// <summary>
    /// The native dialogs get the island window as their owner, so they open above it (the island is always on top), and the island is
    /// held open while they are up, so it cannot collapse under them.
    /// </summary>
    private void AddFolder()
    {
        using var hold = _context.HoldOpen();
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = ModesUiStrings.ChooseFolder };
        if (!Show(dialog.ShowDialog, dialog.ShowDialog) || string.IsNullOrEmpty(dialog.FolderName))
        {
            return;
        }

        var name = Path.GetFileName(dialog.FolderName.TrimEnd('\\', '/'));
        AddItems([new ModeItem { Kind = ModeItemKind.Path, Name = name.Length > 0 ? name : dialog.FolderName, Path = dialog.FolderName }]);
    }

    private void AddFile()
    {
        using var hold = _context.HoldOpen();
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = ModesUiStrings.ChooseFile, CheckFileExists = true };
        if (!Show(dialog.ShowDialog, dialog.ShowDialog) || string.IsNullOrEmpty(dialog.FileName))
        {
            return;
        }

        // Programs and scripts are added as apps, never opened as files.
        if (ModesMath.IsBlockedFile(dialog.FileName))
        {
            ModesDialogKit.Say(_itemsNote, ModesUiStrings.BlockedFile);
            return;
        }

        AddItems([new ModeItem { Kind = ModeItemKind.Path, Name = Path.GetFileName(dialog.FileName), Path = dialog.FileName }]);
    }

    private bool Show(Func<Window, bool?> withOwner, Func<bool?> alone)
    {
        _context.EnsureKeyboard();
        return (Window.GetWindow(this) is { IsVisible: true } owner ? withOwner(owner) : alone()) == true;
    }

    // ------------------------------------------------------------------------------------------ more options

    /// <summary>The folded part: what happens when the mode is left, and its start actions (keep awake, volume, timer), with the Settings editor's values.</summary>
    private void BuildMore()
    {
        _moreHost.Children.Clear();
        var open = _draft.MoreOptions;
        var header = ModesLook.TileButton(
            Row(ModesLook.Text(ModesUiStrings.MoreOptions, 12, FontWeights.Medium, ModesLook.White(0.9)), ModesLook.Icon(open ? Glyphs.ChevronUp : Glyphs.ChevronDown, 10, ModesLook.White(0.7))),
            () =>
            {
                _draft.MoreOptions = !_draft.MoreOptions;
                BuildMore();
                _relayout();
            },
            Id + "MoreOptions",
            ModesUiStrings.MoreOptions,
            true,
            10,
            new Thickness(12, 6, 12, 6),
            pill: true);
        header.HorizontalAlignment = HorizontalAlignment.Left;
        header.Margin = new Thickness(0, 8, 0, 6);
        _moreHost.Children.Add(header);
        if (!open)
        {
            return;
        }

        var mode = _draft.Mode;
        _moreHost.Children.Add(Label(ModesUiStrings.LeaveTitle, 4));
        var leave = new WrapPanel();
        var leaveLabels = new[] { ModesUiStrings.LeaveKeep, ModesUiStrings.LeaveMinimize, ModesUiStrings.LeaveClose };
        for (var i = 0; i < LeaveValues.Length; i++)
        {
            var value = LeaveValues[i];
            leave.Children.Add(Chip(leaveLabels[i], mode.OnLeave == value, () =>
            {
                mode.OnLeave = value;
                BuildMore();
                _relayout();
            }, Id + "OnLeave." + value));
        }

        _moreHost.Children.Add(leave);
        if (mode.OnLeave == "close")
        {
            _moreHost.Children.Add(Wrapped(ModesUiStrings.LeaveCloseCaption, 11, ModesLook.White(0.6)));
        }

        _moreHost.Children.Add(Label(ModesUiStrings.StartTitle));
        if (_context.Runtime.IsAvailable("keep-awake"))
        {
            _moreHost.Children.Add(Caption(ModesUiStrings.KeepAwake));
            _moreHost.Children.Add(ChoiceRow(
                [("off", ModesUiStrings.KeepAwakeOff), ("60", ModesUiStrings.KeepAwake1h), ("120", ModesUiStrings.KeepAwake2h), ("on", ModesUiStrings.KeepAwakeUntil)],
                mode.OnEnter.KeepAwake,
                value => mode.OnEnter.KeepAwake = value,
                Id + "KeepAwake."));
        }
        else
        {
            _moreHost.Children.Add(Caption(ModesUiStrings.KeepAwakeMissing));
        }

        var volumes = new List<(int Value, string Label)>
        {
            (-1, ModesUiStrings.VolumeAsIs), (0, ModesUiStrings.VolumeMute), (25, ModesUiStrings.Percent(25)), (50, ModesUiStrings.Percent(50)),
            (75, ModesUiStrings.Percent(75)), (100, ModesUiStrings.Percent(100)),
        };
        if (volumes.All(v => v.Value != mode.OnEnter.Volume))
        {
            // A level written by hand in modes.json stays selectable instead of silently turning into "as it is".
            volumes.Add((mode.OnEnter.Volume, ModesUiStrings.Percent(mode.OnEnter.Volume)));
        }

        _moreHost.Children.Add(Caption(ModesUiStrings.Volume));
        _moreHost.Children.Add(ChoiceRow(volumes, mode.OnEnter.Volume, value => mode.OnEnter.Volume = value, Id + "Volume."));

        if (_context.Runtime.IsAvailable("notch") && _context.Runtime.IsAvailable("notch-timer"))
        {
            var timer = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            timer.Children.Add(Chip(ModesUiStrings.Timer, mode.OnEnter.Timer, () =>
            {
                mode.OnEnter.Timer = !mode.OnEnter.Timer;
                BuildMore();
            }, Id + "Timer"));
            _moreHost.Children.Add(timer);
        }

        _moreHost.Children.Add(Wrapped(ModesUiStrings.DoNotDisturb, 11, ModesLook.White(0.6)));
    }

    private WrapPanel ChoiceRow<T>(IReadOnlyList<(T Value, string Label)> choices, T current, Action<T> set, string idPrefix)
        where T : notnull
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (value, label) in choices)
        {
            var chosen = value;
            row.Children.Add(Chip(label, EqualityComparer<T>.Default.Equals(value, current), () =>
            {
                set(chosen);
                BuildMore();
            }, idPrefix + value));
        }

        return row;
    }

    // ------------------------------------------------------------------------------------------ small builders

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && row.Children.Count > 0)
            {
                element.Margin = new Thickness(8, 0, 0, 0);
            }

            row.Children.Add(child);
        }

        return row;
    }

    /// <summary>A small section label.</summary>
    private static TextBlock Label(string text, double top = 14)
    {
        var label = ModesLook.Text(text, 11.5, FontWeights.SemiBold, ModesLook.White(0.6));
        label.Margin = new Thickness(0, top, 0, 6);
        return label;
    }

    private static TextBlock Wrapped(string text, double size, Brush brush)
    {
        var block = ModesLook.Text(text, size, null, brush);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    private static TextBlock Caption(string text)
    {
        var caption = Wrapped(text, 11.5, ModesLook.White(0.6));
        caption.Margin = new Thickness(0, 2, 0, 6);
        return caption;
    }

    /// <summary>An orange message line that stays out of the layout while it is empty.</summary>
    private static TextBlock Note()
    {
        var note = Wrapped(string.Empty, 11.5, ModesLook.Of(OrangeColor));
        note.Margin = new Thickness(0, 4, 0, 0);
        note.Visibility = Visibility.Collapsed;
        return note;
    }

    /// <summary>A button in the island's look; the accent one is the main action of its group.</summary>
    private static Button Pill(string text, string id, System.Action click, bool accent = false)
    {
        var label = ModesLook.Text(text, 12, FontWeights.Medium, accent ? Brushes.White : ModesLook.White(0.9));
        return accent
            ? ModesLook.TileButton(label, click, id, text, true, 12, new Thickness(14, 6, 14, 6), pill: true, fill: ModesLook.Of(AccentColor), fillHover: ModesLook.Of(AccentColor, 0.85))
            : ModesLook.TileButton(label, click, id, text, true, 12, new Thickness(14, 6, 14, 6), pill: true);
    }

    /// <summary>A choice among several: ringed in the accent colour while it is the one in use.</summary>
    private static Button Chip(string text, bool selected, System.Action click, string id)
    {
        var label = ModesLook.Text(text, 11.5, selected ? FontWeights.SemiBold : FontWeights.Medium, ModesLook.White(selected ? 1 : 0.8));
        var chip = ModesLook.TileButton(label, click, id, text, true, 12, new Thickness(10, 3, 10, 3), selected ? ModesLook.Of(AccentColor) : null);
        chip.Margin = new Thickness(0, 0, 6, 6);
        return chip;
    }

    /// <summary>A text box in the island's look with a hint that shows while it is empty. Returns the frame; the box comes back in <paramref name="box"/>.</summary>
    private static FrameworkElement Field(out TextBox box, string text, string hint, bool multiline)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var inner = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        inner.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        inner.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        template.VisualTree = inner;

        var field = new TextBox
        {
            Text = text,
            Template = template,
            FontSize = 13,
            FontFamily = UiKit.TextFont,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = ModesLook.Of(AccentColor, 0.6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
            FocusVisualStyle = null,
        };
        if (multiline)
        {
            field.AcceptsReturn = true;
            field.TextWrapping = TextWrapping.Wrap;
            field.MinHeight = 64;
            field.MaxHeight = 130;
        }

        box = field;
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Background = ModesLook.White(0.1),
            Padding = new Thickness(10, 7, 10, 7),
        };
        var grid = new Grid();
        grid.Children.Add(field);
        var hintText = ModesLook.Text(hint, 13, null, ModesLook.White(0.4));
        hintText.IsHitTestVisible = false;
        hintText.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        grid.Children.Add(hintText);
        frame.Child = grid;
        void Sync() => hintText.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        field.TextChanged += (_, _) => Sync();
        field.GotKeyboardFocus += (_, _) => frame.BorderBrush = ModesLook.Of(AccentColor);
        field.LostKeyboardFocus += (_, _) => frame.BorderBrush = Brushes.Transparent;
        Sync();
        return frame;
    }

    /// <summary>A tick box in the island's look: a rounded square that fills with the accent colour when ticked, with the content beside it.</summary>
    private static CheckBox DarkCheck(bool isChecked, UIElement content, string id, string name)
    {
        var template = new ControlTemplate(typeof(CheckBox));
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(DockPanel.LastChildFillProperty, true);
        dock.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

        var box = new FrameworkElementFactory(typeof(Border), "box");
        box.SetValue(DockPanel.DockProperty, Dock.Left);
        box.SetValue(FrameworkElement.WidthProperty, 18.0);
        box.SetValue(FrameworkElement.HeightProperty, 18.0);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        box.SetValue(Border.BorderBrushProperty, ModesLook.White(0.5));
        var tick = new FrameworkElementFactory(typeof(TextBlock), "tick");
        tick.SetValue(TextBlock.TextProperty, Glyphs.Check);
        tick.SetValue(TextBlock.FontFamilyProperty, UiKit.IconFont);
        tick.SetValue(TextBlock.FontSizeProperty, 10.0);
        tick.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(tick);
        dock.AppendChild(box);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        dock.AppendChild(presenter);
        template.VisualTree = dock;

        var on = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, ModesLook.Of(AccentColor), "box"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, ModesLook.Of(AccentColor), "box"));
        on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "tick"));
        template.Triggers.Add(on);

        var check = new CheckBox
        {
            Template = template,
            Content = content,
            IsChecked = isChecked,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(check, id);
        AutomationProperties.SetName(check, name);
        return check;
    }
}
