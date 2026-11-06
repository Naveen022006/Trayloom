// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// A content control that shows up in the UI Automation tree. Plain borders and panels do not, so every row or swatch a test needs to
/// find sits in one of these, which carries its automation id and name.
/// </summary>
internal sealed class ModesLandmark : ContentControl
{
    /// <summary>Wraps <paramref name="content"/> and gives the wrapper its automation id and name.</summary>
    public static ModesLandmark Wrap(UIElement content, string id, string name)
    {
        var landmark = new ModesLandmark { Content = content, Focusable = false, IsTabStop = false };
        AutomationProperties.SetAutomationId(landmark, id);
        AutomationProperties.SetName(landmark, name);
        return landmark;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}

/// <summary>Small building blocks shared by the two Modes dialogs: window chrome, owner, text fields with a hint, and the colour and icon pickers.</summary>
internal static class ModesDialogKit
{
    /// <summary>Theme, size and chrome of a Modes dialog: 560 wide, as tall as its content up to 720 (and never taller than the screen).</summary>
    public static void Style(Window window, double width)
    {
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = Math.Max(360, Math.Min(720, SystemParameters.WorkArea.Height - 40));
        window.ShowInTaskbar = false;
        window.ResizeMode = ResizeMode.NoResize;
        window.FontFamily = UiKit.TextFont;
        window.Themed(Control.BackgroundProperty, ThemeKeys.SettingsWindow);
        window.SourceInitialized += (_, _) => ThemeManager.BindWindow(window);
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally over the window that holds <paramref name="anchor"/>, or over the active window, or in the
    /// middle of the screen when there is none. True when the dialog was accepted.
    /// </summary>
    public static bool ShowOver(Window dialog, DependencyObject? anchor)
    {
        var owner = (anchor is null ? null : Window.GetWindow(anchor))
            ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);

        // An owner that was never shown cannot own a dialog, and a dialog cannot own itself.
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // The Dynamic Island is always on top, so a dialog that is only owned by Settings would open behind it. Staying on top of
        // everything for the few seconds a dialog lives is simpler and safer than collapsing the island under the person's hands.
        dialog.Topmost = true;
        dialog.ContentRendered += (_, _) => dialog.Activate();
        return dialog.ShowDialog() == true;
    }

    /// <summary>A bold section label.</summary>
    public static TextBlock Label(string text, double top = 16)
    {
        var label = UiKit.Text(text, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(0, top, 0, 6);
        return label;
    }

    /// <summary>A secondary caption.</summary>
    public static TextBlock Caption(string text, double top = 6)
    {
        var caption = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, top, 0, 0);
        return caption;
    }

    /// <summary>An orange message line that stays out of the layout while it is empty.</summary>
    public static TextBlock Note(double top = 6)
    {
        var note = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        note.Margin = new Thickness(0, top, 0, 0);
        note.Visibility = Visibility.Collapsed;
        return note;
    }

    /// <summary>Sets an orange note's text and shows or hides it.</summary>
    public static void Say(TextBlock note, string? text)
    {
        note.Text = text ?? string.Empty;
        note.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A themed text box with a hint that shows while it is empty. The box is returned through <paramref name="box"/>.</summary>
    public static FrameworkElement Field(out TextBox box, string text, string hint, bool multiline = false)
    {
        var field = new TextBox { Text = text, FontSize = 13, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
        field.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        if (multiline)
        {
            field.AcceptsReturn = true;
            field.TextWrapping = TextWrapping.Wrap;
            field.MinHeight = 84;
            field.MaxHeight = 160;
            field.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        box = field;
        var grid = new Grid();
        grid.Children.Add(field);
        var hintText = UiKit.Text(hint, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        hintText.IsHitTestVisible = false;
        hintText.Margin = new Thickness(10, multiline ? 7 : 0, 0, 0);
        hintText.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        hintText.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(hintText);
        void Update() => hintText.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        field.TextChanged += (_, _) => Update();
        Update();
        return grid;
    }

    /// <summary>The icon of a kind of item in a rounded tile, or the application's own icon for an app.</summary>
    public static FrameworkElement ItemIcon(ModeItem item, double size = 28)
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
            ModeItemKind.Path when IsFolder(item.Path) => "",
            ModeItemKind.Path => "",
            _ => "",
        };
        return UiKit.IconTile(glyph, size, 13);
    }

    /// <summary>True for a path that is a folder (or no longer exists and has no extension, which is how a folder looks).</summary>
    public static bool IsFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path) || (!File.Exists(path) && !Path.HasExtension(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Puts an automation id and name on the combo box inside a picker row (the row itself is only a layout grid).</summary>
    public static void Identify(DependencyObject root, string id, string name)
    {
        if (FindCombo(root) is { } combo)
        {
            AutomationProperties.SetAutomationId(combo, id);
            AutomationProperties.SetName(combo, name);
        }
    }

    private static ComboBox? FindCombo(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is ComboBox hit)
            {
                return hit;
            }

            if (child is DependencyObject d && FindCombo(d) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

/// <summary>The "Colour" swatches and the "Icon" tiles of a mode (twelve each), shared by the editor and the capture dialog.</summary>
internal sealed class ModeLookPicker : StackPanel
{
    private readonly List<(string Name, ModesLandmark Host, Border Face)> _colours = [];
    private readonly List<(string Glyph, Border Face, TextBlock Icon)> _icons = [];

    public ModeLookPicker(string colour, string glyph, string idPrefix)
    {
        Colour = ModesMath.Colors.Contains(colour) ? colour : ModesMath.Colors[0];
        Glyph = ModesMath.Glyphs.Contains(glyph) ? glyph : ModesMath.Glyphs[0];

        Children.Add(ModesDialogKit.Label(ModesUiStrings.Colour));
        var colours = new TileGrid { MinItemWidth = 30, MaxColumns = 12, Spacing = 6 };
        foreach (var name in ModesMath.Colors)
        {
            var face = new Border
            {
                Height = 30,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Background = ModesLook.Of(ModesLook.ColorOf(name)),
            };
            var colourName = name;
            var host = ModesLandmark.Wrap(face, idPrefix + ".Color." + name, name);
            host.Clickable(() => SelectColour(colourName), name);
            _colours.Add((name, host, face));
            colours.Children.Add(host);
        }

        Children.Add(colours);

        Children.Add(ModesDialogKit.Label(ModesUiStrings.Icon));
        var icons = new TileGrid { MinItemWidth = 30, MaxColumns = 12, Spacing = 6 };
        var number = 0;
        foreach (var glyphName in ModesMath.Glyphs)
        {
            number++;
            var icon = UiKit.Icon(glyphName, 14, ThemeKeys.TextPrimary);
            var face = new Border { Height = 30, CornerRadius = new CornerRadius(8), Child = icon };
            var chosen = glyphName;
            var host = ModesLandmark.Wrap(face, idPrefix + ".Icon." + number, ModesUiStrings.Icon + " " + number);
            host.Clickable(() => SelectGlyph(chosen), ModesUiStrings.Icon + " " + number);
            _icons.Add((glyphName, face, icon));
            icons.Children.Add(host);
        }

        Children.Add(icons);
        Refresh();
    }

    /// <summary>The chosen colour name (one of <see cref="ModesMath.Colors"/>).</summary>
    public string Colour { get; private set; }

    /// <summary>The chosen glyph (one of <see cref="ModesMath.Glyphs"/>).</summary>
    public string Glyph { get; private set; }

    private void SelectColour(string name)
    {
        Colour = name;
        Refresh();
    }

    private void SelectGlyph(string glyph)
    {
        Glyph = glyph;
        Refresh();
    }

    /// <summary>Draws the ring on the chosen colour and fills the chosen icon with it.</summary>
    private void Refresh()
    {
        var current = ModesLook.Of(ModesLook.ColorOf(Colour));
        foreach (var (name, _, face) in _colours)
        {
            if (name == Colour)
            {
                face.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.TextPrimary);
            }
            else
            {
                face.ClearValue(Border.BorderBrushProperty);
                face.BorderBrush = Brushes.Transparent;
            }
        }

        foreach (var (glyph, face, icon) in _icons)
        {
            if (glyph == Glyph)
            {
                face.ClearValue(Border.BackgroundProperty);
                face.Background = current;
                icon.ClearValue(TextBlock.ForegroundProperty);
                icon.Foreground = Brushes.White;
            }
            else
            {
                face.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
                icon.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
            }
        }
    }
}

/// <summary>
/// The "New mode" / "Edit mode" sheet (spec 08 section 2.6): name, colour and icon, the items a mode opens, what happens when it is left
/// and started, and its shortcut. It works on a copy; nothing changes until Save, which hands the copy to <see cref="ModesRuntime"/>.
/// </summary>
internal sealed class ModeEditorWindow : Window
{
    private static readonly string[] ZoneRaws =
    [
        string.Empty, "maximize", "leftHalf", "rightHalf", "leftThird", "centerThird", "rightThird", "leftTwoThirds", "rightTwoThirds",
        "topLeft", "topRight", "bottomLeft", "bottomRight",
    ];

    private readonly ModesRuntime _runtime;
    private readonly IContributionServices _services;
    private readonly Mode _mode;
    private readonly bool _isNew;
    private readonly TextBox _name;
    private readonly TextBlock _nameNote = ModesDialogKit.Note();
    private readonly ModeLookPicker _look;
    private readonly StackPanel _itemsHost = new();
    private readonly TextBlock _itemsNote = ModesDialogKit.Note();
    private readonly WrapPanel _addButtons = new();
    private readonly Border _webPanel = new();
    private readonly Button _save;
    private IReadOnlyList<BrowserChoice>? _browsers;

    // The inline "Add web pages" panel
    private ComboBox? _webBrowser;
    private ComboBox? _webProfile;
    private FrameworkElement? _webProfileRow;
    private TextBox? _webText;
    private Button? _webTabsButton;
    private readonly TextBlock _webNote = ModesDialogKit.Note();
    private readonly StackPanel _webTabs = new() { Visibility = Visibility.Collapsed };
    private List<(string Dir, string Name)> _profiles = [];
    private bool _closed;
    private bool _touched;

    public ModeEditorWindow(ModesRuntime runtime, IContributionServices services, Mode? existing)
    {
        _runtime = runtime;
        _services = services;
        _isNew = existing is null;
        _mode = existing?.Clone() ?? new Mode { Color = ModesMath.Colors[0], Glyph = ModesMath.Glyphs[0], OnLeave = "minimize" };
        Title = _isNew ? ModesUiStrings.EditorNew : ModesUiStrings.EditorEdit;
        ModesDialogKit.Style(this, 560);
        Closed += (_, _) => _closed = true;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(UiKit.Text(Title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));

        _save = UiKit.Button(ModesUiStrings.Save, ButtonKind.Prominent, 12, null, Commit);
        AutomationProperties.SetAutomationId(_save, "ModeEditor.Save");
        _look = new ModeLookPicker(_mode.Color, _mode.Glyph, "ModeEditor");

        var body = new StackPanel { Margin = new Thickness(0, 14, 8, 0) };
        _name = BuildName(body);
        body.Children.Add(_look);
        BuildItems(body);
        BuildLeave(body);
        BuildStart(body);
        BuildShortcut(body);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = UiKit.Button(ModesUiStrings.Cancel, ButtonKind.Bordered, 12, null, () => DialogResult = false);
        AutomationProperties.SetAutomationId(cancel, "ModeEditor.Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        RebuildItems();
        Validate();
        Loaded += (_, _) =>
        {
            if (_isNew)
            {
                _name.Focus();
            }
        };
    }

    /// <summary>The mode as it was stored, once Save succeeded.</summary>
    public Mode? Saved { get; private set; }

    // ------------------------------------------------------------------------------------------ name

    private TextBox BuildName(StackPanel body)
    {
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.Name, 0));
        body.Children.Add(ModesDialogKit.Field(out var box, _mode.Name, ModesUiStrings.NamePlaceholder));
        box.MaxLength = ModesMath.MaxNameLength;
        AutomationProperties.SetAutomationId(box, "ModeEditor.Name");
        AutomationProperties.SetName(box, ModesUiStrings.Name);
        box.TextChanged += (_, _) =>
        {
            _touched = true;
            Validate();
        };
        body.Children.Add(_nameNote);
        return box;
    }

    /// <summary>The name problem, or null when the name is fine.</summary>
    private string? NameProblem()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            return ModesUiStrings.NameMissing;
        }

        var taken = _runtime.Modes.Any(m => m.Id != _mode.Id && string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        return taken ? ModesUiStrings.NameTaken : null;
    }

    private void Validate()
    {
        // The empty-name message waits until the person has typed or pressed Save, so a fresh sheet does not open with a complaint.
        var problem = NameProblem();
        var quiet = !_touched && problem == ModesUiStrings.NameMissing;
        ModesDialogKit.Say(_nameNote, quiet ? null : problem);
        _save.IsEnabled = problem is null;
    }

    // ------------------------------------------------------------------------------------------ items

    private void BuildItems(StackPanel body)
    {
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.WhatOpens));
        body.Children.Add(_itemsHost);
        body.Children.Add(_itemsNote);

        _addButtons.Margin = new Thickness(0, 8, 0, 0);
        AddButton("ModeEditor.AddApp", ModesUiStrings.AddApp, "", AddApp);
        AddButton("ModeEditor.AddFolder", ModesUiStrings.AddFolder, "", AddFolder);
        AddButton("ModeEditor.AddFile", ModesUiStrings.AddFile, "", AddFile);
        AddButton("ModeEditor.AddWeb", ModesUiStrings.AddWeb, "", ToggleWebPanel);
        body.Children.Add(_addButtons);

        _webPanel.Visibility = Visibility.Collapsed;
        _webPanel.CornerRadius = new CornerRadius(10);
        _webPanel.Padding = new Thickness(12);
        _webPanel.Margin = new Thickness(0, 8, 0, 0);
        _webPanel.Themed(Border.BackgroundProperty, ThemeKeys.TileFill);
        body.Children.Add(_webPanel);
    }

    private void AddButton(string id, string text, string glyph, Action click)
    {
        var button = UiKit.Button(text, ButtonKind.Bordered, 12, glyph, click);
        button.Margin = new Thickness(0, 0, 8, 6);
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, text);
        _addButtons.Children.Add(button);
    }

    private void RebuildItems()
    {
        _itemsHost.Children.Clear();
        EnsureFront();
        if (_mode.Items.Count == 0)
        {
            _itemsHost.Children.Add(ModesDialogKit.Caption(ModesUiStrings.NothingYet, 0));
        }

        for (var i = 0; i < _mode.Items.Count; i++)
        {
            _itemsHost.Children.Add(ItemRow(_mode.Items[i], i));
        }

        var full = _mode.Items.Count >= ModesMath.MaxItems;
        foreach (var child in _addButtons.Children.OfType<Button>())
        {
            child.IsEnabled = !full;
        }

        if (full)
        {
            ModesDialogKit.Say(_itemsNote, ModesUiStrings.ItemLimit);
        }
    }

    /// <summary>Exactly one item is brought to the front: the one marked, else the first.</summary>
    private void EnsureFront()
    {
        if (_mode.Items.Count == 0)
        {
            return;
        }

        var marked = _mode.Items.FirstOrDefault(i => i.Front) ?? _mode.Items[0];
        foreach (var item in _mode.Items)
        {
            item.Front = ReferenceEquals(item, marked);
        }
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

    private FrameworkElement ItemRow(ModeItem item, int index)
    {
        var number = index + 1;
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
        card.Themed(Border.BackgroundProperty, ThemeKeys.TileFill);

        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(ModesDialogKit.ItemIcon(item));

        var detail = DetailOf(item);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(item.Name, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text(detail, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        text.ToolTip = detail;
        Grid.SetColumn(text, 2);
        top.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var front = ShortcutUi.SmallIconButton(item.Front ? "" : "", ModesUiStrings.BringToFront);
        if (item.Front && front.Content is TextBlock star)
        {
            star.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
        }

        front.Click += (_, _) =>
        {
            foreach (var other in _mode.Items)
            {
                other.Front = ReferenceEquals(other, item);
            }

            RebuildItems();
        };
        AutomationProperties.SetAutomationId(front, "ModeEditor.Item." + number + ".Front");
        buttons.Children.Add(front);

        var up = ShortcutUi.SmallIconButton("", ModesUiStrings.MoveUp);
        up.IsEnabled = index > 0;
        up.Opacity = up.IsEnabled ? 1 : 0.4;
        up.Click += (_, _) => MoveItem(index, -1);
        AutomationProperties.SetAutomationId(up, "ModeEditor.Item." + number + ".Up");
        buttons.Children.Add(up);

        var down = ShortcutUi.SmallIconButton("", ModesUiStrings.MoveDown);
        down.IsEnabled = index < _mode.Items.Count - 1;
        down.Opacity = down.IsEnabled ? 1 : 0.4;
        down.Click += (_, _) => MoveItem(index, 1);
        AutomationProperties.SetAutomationId(down, "ModeEditor.Item." + number + ".Down");
        buttons.Children.Add(down);

        var remove = ShortcutUi.SmallIconButton("", ModesUiStrings.Remove);
        remove.Click += (_, _) =>
        {
            _mode.Items.Remove(item);
            ModesDialogKit.Say(_itemsNote, null);
            RebuildItems();
        };
        AutomationProperties.SetAutomationId(remove, "ModeEditor.Item." + number + ".Remove");
        buttons.Children.Add(remove);
        Grid.SetColumn(buttons, 3);
        top.Children.Add(buttons);
        stack.Children.Add(top);

        // Placement, and for an app the arguments. A wrap panel lets the second line break instead of overflowing.
        var options = new WrapPanel { Margin = new Thickness(38, 8, 0, 0) };
        var zone = ZoneCombo(item, number);
        zone.Margin = new Thickness(0, 0, 8, 4);
        options.Children.Add(zone);
        if (item.Kind == ModeItemKind.App && string.IsNullOrEmpty(item.Aumid) && !string.IsNullOrEmpty(item.Exe))
        {
            var args = ModesDialogKit.Field(out var argsBox, item.Args, ModesUiStrings.ArgsPlaceholder);
            argsBox.FontSize = 12;
            args.Width = 210;
            args.Margin = new Thickness(0, 0, 8, 4);
            argsBox.TextChanged += (_, _) => item.Args = argsBox.Text;
            AutomationProperties.SetAutomationId(argsBox, "ModeEditor.Item." + number + ".Args");
            AutomationProperties.SetName(argsBox, ModesUiStrings.ArgsPlaceholder);
            options.Children.Add(args);

            var exe = item.Exe;
            var withFolder = UiKit.Button(ModesUiStrings.OpenWithFolder, ButtonKind.Bordered, 11.5, null, () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = ModesUiStrings.ChooseFolder };
                if (dialog.ShowDialog(this) == true && !string.IsNullOrEmpty(dialog.FolderName))
                {
                    item.Args = ModesMath.ArgsForFolder(exe, dialog.FolderName, out var dir, item.Args);
                    item.Dir = dir;
                    argsBox.Text = item.Args;
                }
            });
            withFolder.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetAutomationId(withFolder, "ModeEditor.Item." + number + ".OpenWithFolder");
            options.Children.Add(withFolder);
        }

        stack.Children.Add(options);
        card.Child = stack;
        return ModesLandmark.Wrap(card, "ModeEditor.Item." + number, item.Name);
    }

    private void MoveItem(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _mode.Items.Count)
        {
            return;
        }

        (_mode.Items[index], _mode.Items[target]) = (_mode.Items[target], _mode.Items[index]);
        RebuildItems();
    }

    /// <summary>"Where it opens" and the zones a mode item may use; a zone the list does not offer (from a capture) stays selectable.</summary>
    private static ComboBox ZoneCombo(ModeItem item, int number)
    {
        var raws = new List<string>(ZoneRaws);
        if (!string.IsNullOrEmpty(item.Zone) && !raws.Contains(item.Zone))
        {
            raws.Add(item.Zone);
        }

        var labels = raws.Select(r => r.Length == 0 ? ModesUiStrings.WhereItOpens : LayoutActions.Get(r)?.Title ?? r).ToList();
        var combo = new ComboBox { MinWidth = 150, ItemsSource = labels, ToolTip = ModesUiStrings.Place };
        var selected = raws.IndexOf(item.Zone ?? string.Empty);
        combo.SelectedIndex = selected < 0 ? 0 : selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                item.Zone = raws[combo.SelectedIndex].Length == 0 ? null : raws[combo.SelectedIndex];
            }
        };
        AutomationProperties.SetAutomationId(combo, "ModeEditor.Item." + number + ".Zone");
        AutomationProperties.SetName(combo, ModesUiStrings.Place);
        return combo;
    }

    /// <summary>Adds items (at most twenty in all, no repeats) and shows what was left out.</summary>
    private void AddItems(IEnumerable<ModeItem> items)
    {
        ModesDialogKit.Say(_itemsNote, null);
        foreach (var item in items)
        {
            if (_mode.Items.Count >= ModesMath.MaxItems)
            {
                ModesDialogKit.Say(_itemsNote, ModesUiStrings.ItemLimit);
                break;
            }

            item.Id = ModesMath.NewItemId(_mode);
            _mode.Items.Add(item);
        }

        _mode.Items = ModesMath.Dedupe(_mode.Items);
        RebuildItems();
    }

    private void AddApp()
    {
        var picker = new AppPickerWindow(null, [], allowBrowse: true);
        if (!ModesDialogKit.ShowOver(picker, this) || picker.Result is not { Length: > 0 } exe)
        {
            return;
        }

        AddItems([new ModeItem { Kind = ModeItemKind.App, Name = AppIdentity.DisplayName(exe), Exe = exe.ToLowerInvariant() }]);
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = ModesUiStrings.ChooseFolder };
        if (dialog.ShowDialog(this) != true || string.IsNullOrEmpty(dialog.FolderName))
        {
            return;
        }

        var name = Path.GetFileName(dialog.FolderName.TrimEnd('\\', '/'));
        AddItems([new ModeItem { Kind = ModeItemKind.Path, Name = name.Length > 0 ? name : dialog.FolderName, Path = dialog.FolderName }]);
    }

    private void AddFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = ModesUiStrings.ChooseFile, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || string.IsNullOrEmpty(dialog.FileName))
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

    // ------------------------------------------------------------------------------------------ web pages

    private void ToggleWebPanel()
    {
        if (_webPanel.Visibility == Visibility.Visible)
        {
            _webPanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (_webPanel.Child is null)
        {
            _webPanel.Child = BuildWebPanel();
        }

        _webPanel.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildWebPanel()
    {
        var panel = new StackPanel();

        // Browser and profile sit side by side while there is room and wrap under each other when there is not.
        var pickers = new WrapPanel();
        var choices = Browsers();
        _webBrowser = new ComboBox { MinWidth = 190, ItemsSource = choices.Select(b => b.Name).ToList(), SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(_webBrowser, "ModeEditor.Web.Browser");
        AutomationProperties.SetName(_webBrowser, ModesUiStrings.Browser);
        pickers.Children.Add(Labelled(ModesUiStrings.Browser, _webBrowser));

        _webProfile = new ComboBox { MinWidth = 190 };
        AutomationProperties.SetAutomationId(_webProfile, "ModeEditor.Web.Profile");
        AutomationProperties.SetName(_webProfile, ModesUiStrings.Profile);
        _webProfileRow = Labelled(ModesUiStrings.Profile, _webProfile);
        pickers.Children.Add(_webProfileRow);
        panel.Children.Add(pickers);
        _webBrowser.SelectionChanged += (_, _) => FillProfiles();
        FillProfiles();

        var label = UiKit.Text(ModesUiStrings.PasteAddresses, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        label.Margin = new Thickness(0, 8, 0, 4);
        panel.Children.Add(label);
        var field = ModesDialogKit.Field(out var text, string.Empty, ModesUiStrings.PasteAddresses, multiline: true);
        _webText = text;
        text.FontSize = 12;
        AutomationProperties.SetAutomationId(text, "ModeEditor.Web.Addresses");
        AutomationProperties.SetName(text, ModesUiStrings.PasteAddresses);
        panel.Children.Add(field);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var add = UiKit.Button(ModesUiStrings.AddAddresses, ButtonKind.Prominent, 12, null, AddPasted);
        add.Margin = new Thickness(0, 0, 8, 4);
        AutomationProperties.SetAutomationId(add, "ModeEditor.Web.Add");
        buttons.Children.Add(add);
        _webTabsButton = UiKit.Button(ModesUiStrings.AddCurrentTabs, ButtonKind.Bordered, 12, null, ReadTabs);
        _webTabsButton.Margin = new Thickness(0, 0, 8, 4);
        AutomationProperties.SetAutomationId(_webTabsButton, "ModeEditor.Web.Tabs");
        buttons.Children.Add(_webTabsButton);
        var done = UiKit.Button(ModesUiStrings.Done, ButtonKind.Plain, 12, null, () => _webPanel.Visibility = Visibility.Collapsed);
        done.Margin = new Thickness(0, 0, 0, 4);
        AutomationProperties.SetAutomationId(done, "ModeEditor.Web.Done");
        buttons.Children.Add(done);
        panel.Children.Add(buttons);

        panel.Children.Add(_webNote);
        panel.Children.Add(_webTabs);
        panel.Children.Add(ModesDialogKit.Caption(ModesUiStrings.TabsCaption, 8));
        return panel;
    }

    private static FrameworkElement Labelled(string label, FrameworkElement control)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
        stack.Children.Add(UiKit.Text(label, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        control.Margin = new Thickness(0, 4, 0, 0);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(control);
        return stack;
    }

    private BrowserChoice? SelectedBrowser()
    {
        var index = _webBrowser?.SelectedIndex ?? -1;
        var choices = Browsers();
        return index >= 0 && index < choices.Count ? choices[index] : null;
    }

    /// <summary>The profile folder chosen in the panel; empty means the browser's own choice.</summary>
    private string SelectedProfile()
    {
        var index = _webProfile?.SelectedIndex ?? -1;
        return index > 0 && index - 1 < _profiles.Count ? _profiles[index - 1].Dir : string.Empty;
    }

    /// <summary>Lists the profiles of the chosen Chromium browser; for any other browser the row is hidden.</summary>
    private void FillProfiles()
    {
        if (_webProfile is null || _webProfileRow is null)
        {
            return;
        }

        var browser = SelectedBrowser();
        _profiles = browser is { Chromium: true } ? [.. _runtime.ChromiumProfiles(browser.Id)] : [];
        _webProfileRow.Visibility = _profiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _webProfile.ItemsSource = new[] { ModesUiStrings.BrowserOwnChoice }.Concat(_profiles.Select(p => p.Name)).ToList();
        _webProfile.SelectedIndex = 0;
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host) : url;

    private ModeItem WebItem(string name, IReadOnlyList<string> urls) => new()
    {
        Kind = ModeItemKind.Web,
        Name = name,
        Browser = SelectedBrowser()?.Id ?? "default",
        Profile = SelectedProfile(),
        Urls = [.. urls],
    };

    private void AddPasted()
    {
        var urls = ModesMath.ParseUrlList(_webText?.Text, out var rejected);
        if (urls.Count == 0)
        {
            ModesDialogKit.Say(_webNote, rejected > 0 ? ModesUiStrings.Rejected(rejected) : ModesUiStrings.NoValidAddresses);
            return;
        }

        ModesDialogKit.Say(_webNote, rejected > 0 ? ModesUiStrings.Rejected(rejected) : null);
        var host = HostOf(urls[0]);
        AddItems([WebItem(urls.Count > 1 ? ModesUiStrings.AndMore(host, urls.Count - 1) : host, urls)]);
        _webText!.Text = string.Empty;
    }

    /// <summary>Reads the open tabs of the chosen Chrome or Edge profile (on this click only) and lists the windows to tick.</summary>
    private async void ReadTabs()
    {
        var browser = SelectedBrowser();
        if (browser is null || _webTabsButton is null)
        {
            return;
        }

        try
        {
            _webTabsButton.IsEnabled = false;
            _webTabs.Visibility = Visibility.Collapsed;
            ModesDialogKit.Say(_webNote, ModesUiStrings.ReadingTabs);
            var result = await _runtime.ReadBrowserTabsAsync(browser.Id, SelectedProfile());
            if (_closed)
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
            ShowTabs(browser, result.Windows);
        }
        catch (Exception ex)
        {
            _services.Log.Info($"[modes] reading tabs failed: {ex.GetType().Name}");
            ModesDialogKit.Say(_webNote, ModesStrings.TabsUnreadable(browser.Name));
        }
        finally
        {
            _webTabsButton.IsEnabled = true;
        }
    }

    /// <summary>A checklist with one line per browser window (its tab count and the first sites), all ticked.</summary>
    private void ShowTabs(BrowserChoice browser, IReadOnlyList<IReadOnlyList<string>> windows)
    {
        _webTabs.Children.Clear();
        var boxes = new List<(CheckBox Box, IReadOnlyList<string> Urls, int Number)>();
        var number = 0;
        foreach (var urls in windows)
        {
            number++;
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            var sites = urls.Select(HostOf).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            var line = new StackPanel();
            line.Children.Add(UiKit.Text(ModesUiStrings.WebWindow(number) + ModesUiStrings.Dot + ModesStrings.TabCount(urls.Count), 12.5, FontWeights.Medium, ThemeKeys.TextPrimary));
            line.Children.Add(UiKit.Text(string.Join(", ", sites) + (urls.Count > sites.Count ? "…" : string.Empty), 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(box);
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            AutomationProperties.SetAutomationId(box, "ModeEditor.Web.Window." + number);
            AutomationProperties.SetName(box, ModesUiStrings.WebWindow(number));
            _webTabs.Children.Add(row);
            boxes.Add((box, urls, number));
        }

        var addTicked = UiKit.Button(ModesUiStrings.AddSelected, ButtonKind.Prominent, 12, null, () =>
        {
            var items = new List<ModeItem>();
            foreach (var (box, urls, windowNumber) in boxes.Where(b => b.Box.IsChecked == true))
            {
                var clean = urls.Select(ModesMath.NormalizeUrl).OfType<string>().ToList();
                foreach (var group in ModesMath.GroupUrls(clean))
                {
                    items.Add(WebItem(ModesStrings.BrowserWindow(browser.Name, windowNumber), group));
                }

                box.IsChecked = false;
            }

            if (items.Count > 0)
            {
                AddItems(items);
            }
        });
        addTicked.Margin = new Thickness(0, 4, 0, 0);
        addTicked.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(addTicked, "ModeEditor.Web.AddTicked");
        _webTabs.Children.Add(addTicked);
        _webTabs.Margin = new Thickness(0, 10, 0, 0);
        _webTabs.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------------------------------ leave and start

    private void BuildLeave(StackPanel body)
    {
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.LeaveTitle));
        var combo = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        var values = new[] { "keep", "minimize", "close" };
        combo.ItemsSource = new[] { ModesUiStrings.LeaveKeep, ModesUiStrings.LeaveMinimize, ModesUiStrings.LeaveClose };
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, _mode.OnLeave));
        AutomationProperties.SetAutomationId(combo, "ModeEditor.OnLeave");
        AutomationProperties.SetName(combo, ModesUiStrings.LeaveTitle);
        body.Children.Add(combo);
        var caption = ModesDialogKit.Caption(ModesUiStrings.LeaveCloseCaption);
        caption.Visibility = combo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        body.Children.Add(caption);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                _mode.OnLeave = values[combo.SelectedIndex];
                caption.Visibility = combo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            }
        };
    }

    private void BuildStart(StackPanel body)
    {
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.StartTitle));

        var keepAwakeAvailable = _services.Runtime.IsAvailable("keep-awake");
        var keepAwake = new PickerRow<string>(null, ModesUiStrings.KeepAwake, null,
        [
            ("off", ModesUiStrings.KeepAwakeOff), ("60", ModesUiStrings.KeepAwake1h), ("120", ModesUiStrings.KeepAwake2h), ("on", ModesUiStrings.KeepAwakeUntil),
        ], _mode.OnEnter.KeepAwake, value => _mode.OnEnter.KeepAwake = value, 200)
        {
            IsEnabled = keepAwakeAvailable,
            Opacity = keepAwakeAvailable ? 1 : 0.45,
        };
        ModesDialogKit.Identify(keepAwake, "ModeEditor.KeepAwake", ModesUiStrings.KeepAwake);
        body.Children.Add(keepAwake);
        if (!keepAwakeAvailable)
        {
            body.Children.Add(ModesDialogKit.Caption(ModesUiStrings.KeepAwakeMissing, 2));
        }

        var volumes = new List<(int Value, string Label)>
        {
            (-1, ModesUiStrings.VolumeAsIs), (0, ModesUiStrings.VolumeMute), (25, ModesUiStrings.Percent(25)), (50, ModesUiStrings.Percent(50)),
            (75, ModesUiStrings.Percent(75)), (100, ModesUiStrings.Percent(100)),
        };
        if (volumes.All(v => v.Value != _mode.OnEnter.Volume))
        {
            // A level written by hand in modes.json stays selectable instead of silently turning into "as it is".
            volumes.Add((_mode.OnEnter.Volume, ModesUiStrings.Percent(_mode.OnEnter.Volume)));
        }

        var volume = new PickerRow<int>(null, ModesUiStrings.Volume, null, volumes, _mode.OnEnter.Volume, value => _mode.OnEnter.Volume = value, 200);
        volume.Margin = new Thickness(0, 8, 0, 0);
        ModesDialogKit.Identify(volume, "ModeEditor.Volume", ModesUiStrings.Volume);
        body.Children.Add(volume);

        if (_services.Runtime.IsAvailable("notch") && _services.Runtime.IsAvailable("notch-timer"))
        {
            var toggle = new MacSwitch(_mode.OnEnter.Timer, SwitchSize.Regular, on => _mode.OnEnter.Timer = on);
            AutomationProperties.SetAutomationId(toggle, "ModeEditor.Timer");
            AutomationProperties.SetName(toggle, ModesUiStrings.Timer);
            var row = new SettingsRow(null, ModesUiStrings.Timer, null, toggle) { Margin = new Thickness(0, 8, 0, 0) };
            body.Children.Add(row);
        }

        body.Children.Add(ModesDialogKit.Caption(ModesUiStrings.DoNotDisturb, 8));
    }

    private void BuildShortcut(StackPanel body)
    {
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.Shortcut));
        var roleId = ModesRuntime.RoleId(_mode.Id);

        // The shortcut belongs to a stored mode (its role is made when the mode is added), so a new mode explains why there is no recorder yet.
        if (_isNew || string.IsNullOrEmpty(_mode.Id) || _services.Shortcuts.Role(roleId) is null)
        {
            body.Children.Add(ModesDialogKit.Caption(ModesUiStrings.ShortcutSaveFirst, 0));
            return;
        }

        var row = ShortcutUi.LabelledRow(_services.Shortcuts, roleId, "", ModesUiStrings.Shortcut, clearable: true);
        body.Children.Add(ModesLandmark.Wrap(row, "ModeEditor.Shortcut", ModesUiStrings.Shortcut));
    }

    // ------------------------------------------------------------------------------------------ save

    private void Commit()
    {
        _touched = true;
        Validate();
        if (!_save.IsEnabled)
        {
            return;
        }

        _mode.Name = _name.Text.Trim();
        _mode.Color = _look.Colour;
        _mode.Glyph = _look.Glyph;
        EnsureFront();
        if (_isNew)
        {
            if (!_runtime.CanAdd)
            {
                ModesDialogKit.Say(_nameNote, ModesUiStrings.LimitReached);
                return;
            }

            Saved = _runtime.Add(_mode);
        }
        else
        {
            _runtime.Update(_mode);
            Saved = _mode;
        }

        DialogResult = true;
    }
}
