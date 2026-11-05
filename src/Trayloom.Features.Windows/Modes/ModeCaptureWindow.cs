// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// "Save what's open now" (spec 08 section 2.6): reads the open apps, browser windows and folders into memory, lists them with
/// checkboxes and warnings, and saves a mode only when the person confirms. Nothing is written before that.
/// </summary>
internal sealed class ModeCaptureWindow : Window
{
    private readonly ModesRuntime _runtime;
    private readonly IContributionServices _services;
    private readonly TextBox _name;
    private readonly TextBlock _nameNote = ModesDialogKit.Note();
    private readonly ModeLookPicker _look;
    private readonly TextBlock _status;
    private readonly StackPanel _groups = new();
    private readonly StackPanel _warnings = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _saveNote = ModesDialogKit.Note(10);
    private readonly Button _save;
    private readonly List<(CapturedItem Captured, CheckBox Box)> _rows = [];
    private bool _loaded;
    private bool _closed;
    private bool _touched;

    public ModeCaptureWindow(ModesRuntime runtime, IContributionServices services)
    {
        _runtime = runtime;
        _services = services;
        Title = ModesUiStrings.CaptureTitle;
        ModesDialogKit.Style(this, 560);
        Closed += (_, _) => _closed = true;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(UiKit.Text(Title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        header.Children.Add(ModesDialogKit.Caption(ModesUiStrings.CaptureCaption, 6));
        root.Children.Add(header);

        _save = UiKit.Button(ModesUiStrings.CaptureSave, ButtonKind.Prominent, 12, null, Commit);
        _save.IsEnabled = false;
        AutomationProperties.SetAutomationId(_save, "ModeCapture.Save");

        var body = new StackPanel { Margin = new Thickness(0, 14, 8, 0) };
        body.Children.Add(ModesDialogKit.Label(ModesUiStrings.Name, 0));
        body.Children.Add(ModesDialogKit.Field(out var box, UniqueDefaultName(), ModesUiStrings.CaptureDefaultName));
        _name = box;
        _name.MaxLength = ModesMath.MaxNameLength;
        AutomationProperties.SetAutomationId(_name, "ModeCapture.Name");
        AutomationProperties.SetName(_name, ModesUiStrings.Name);
        _name.TextChanged += (_, _) =>
        {
            _touched = true;
            Refresh();
        };
        body.Children.Add(_nameNote);

        _look = new ModeLookPicker(ModesMath.Colors[0], ModesMath.Glyphs[0], "ModeCapture");
        body.Children.Add(_look);

        _status = UiKit.Text(ModesUiStrings.CaptureReading, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        _status.Margin = new Thickness(0, 16, 0, 0);
        AutomationProperties.SetAutomationId(_status, "ModeCapture.Status");
        body.Children.Add(_status);
        body.Children.Add(_groups);
        body.Children.Add(_warnings);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(_saveNote);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = UiKit.Button(ModesUiStrings.Cancel, ButtonKind.Bordered, 12, null, () => DialogResult = false);
        AutomationProperties.SetAutomationId(cancel, "ModeCapture.Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        Refresh();
        Loaded += (_, _) => Load();
    }

    /// <summary>The mode as it was stored, once "Save mode" succeeded.</summary>
    public Mode? Saved { get; private set; }

    /// <summary>"My mode", or "My mode 2", "My mode 3"... when the plain name is taken.</summary>
    private string UniqueDefaultName()
    {
        var taken = _runtime.Modes.Select(m => m.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = ModesUiStrings.CaptureDefaultName;
        var name = baseName;
        for (var n = 2; taken.Contains(name); n++)
        {
            name = baseName + " " + n;
        }

        return name;
    }

    /// <summary>Reads what is open (on a worker thread) and fills the lists. A failure is shown in the dialog and logged without details.</summary>
    private async void Load()
    {
        try
        {
            var capture = await _runtime.CaptureAsync();
            if (_closed)
            {
                return;
            }

            Fill(capture);
        }
        catch (Exception ex)
        {
            _services.Log.Info($"[modes] capture failed: {ex.GetType().Name}");
            if (!_closed)
            {
                _status.Text = ModesUiStrings.CaptureFailed;
            }
        }
    }

    private void Fill(ModeCapture capture)
    {
        _loaded = true;
        _groups.Children.Clear();
        _rows.Clear();
        _status.Visibility = capture.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = ModesUiStrings.CaptureNothing;

        AddGroup(ModesUiStrings.CaptureApps, capture.Items.Where(c => c.Item.Kind == ModeItemKind.App), "Apps");
        AddGroup(ModesUiStrings.CaptureBrowserWindows, capture.Items.Where(c => c.Item.Kind == ModeItemKind.Web), "Browsers");
        AddGroup(ModesUiStrings.CaptureFolders, capture.Items.Where(c => c.Item.Kind == ModeItemKind.Path), "Folders");

        _warnings.Children.Clear();
        foreach (var warning in capture.Warnings)
        {
            var line = UiKit.Text(warning, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
            line.Margin = new Thickness(0, 0, 0, 4);
            _warnings.Children.Add(line);
        }

        Refresh();
    }

    private void AddGroup(string title, IEnumerable<CapturedItem> items, string idPart)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var heading = ModesDialogKit.Label(title);
        _groups.Children.Add(heading);
        var number = 0;
        foreach (var captured in list)
        {
            number++;
            _groups.Children.Add(Row(captured, "ModeCapture." + idPart + "." + number));
        }
    }

    /// <summary>One line: a checkbox with the icon, the name and the detail; a browser window can also show its addresses.</summary>
    private FrameworkElement Row(CapturedItem captured, string id)
    {
        var item = captured.Item;
        var box = new CheckBox { IsChecked = captured.Selected, VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        box.Click += (_, _) => Refresh();

        var line = new Grid { Margin = new Thickness(4, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = ModesDialogKit.ItemIcon(item, 26);
        line.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(item.Name, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        if (captured.Detail.Length > 0)
        {
            text.Children.Add(UiKit.Text(captured.Detail, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        }

        Grid.SetColumn(text, 2);
        line.Children.Add(text);
        box.Content = line;

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(box);
        if (item.Kind == ModeItemKind.Web && item.Urls.Count > 0)
        {
            row.Children.Add(AddressesToggle(item));
        }

        _rows.Add((captured, box));
        AutomationProperties.SetAutomationId(box, id);
        AutomationProperties.SetName(box, item.Name);
        return row;
    }

    /// <summary>A link that shows or hides the host and path of each address of a browser window (the query is never shown).</summary>
    private static FrameworkElement AddressesToggle(ModeItem item)
    {
        var host = new StackPanel { Margin = new Thickness(38, 2, 0, 0) };
        var list = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var url in item.Urls)
        {
            list.Children.Add(UiKit.Text(Short(url), 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        }

        var toggle = UiKit.Text(ModesUiStrings.ShowAddresses, 11.5, FontWeights.Normal, ThemeKeys.Accent);
        toggle.Clickable(() =>
        {
            var show = list.Visibility != Visibility.Visible;
            list.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            toggle.Text = show ? ModesUiStrings.HideAddresses : ModesUiStrings.ShowAddresses;
        }, ModesUiStrings.ShowAddresses);
        host.Children.Add(toggle);
        host.Children.Add(list);
        return host;
    }

    private static string Short(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;
        return uri.Host + path;
    }

    /// <summary>Updates the messages and the Save button from the name and the ticks.</summary>
    private void Refresh()
    {
        var name = _name.Text.Trim();
        string? nameProblem = null;
        if (name.Length == 0)
        {
            nameProblem = ModesUiStrings.NameMissing;
        }
        else if (_runtime.Modes.Any(m => string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            nameProblem = ModesUiStrings.NameTaken;
        }

        ModesDialogKit.Say(_nameNote, !_touched && nameProblem == ModesUiStrings.NameMissing ? null : nameProblem);
        var ticked = _rows.Count(r => r.Box.IsChecked == true);
        ModesDialogKit.Say(_saveNote, _loaded && _rows.Count > 0 && ticked == 0 ? ModesUiStrings.CaptureNothingTicked : null);
        _save.IsEnabled = _loaded && nameProblem is null && ticked > 0;
    }

    private void Commit()
    {
        _touched = true;
        Refresh();
        if (!_save.IsEnabled)
        {
            return;
        }

        if (!_runtime.CanAdd)
        {
            ModesDialogKit.Say(_saveNote, ModesUiStrings.LimitReached);
            return;
        }

        var mode = new Mode { Name = _name.Text.Trim(), Color = _look.Colour, Glyph = _look.Glyph, OnLeave = "minimize" };
        foreach (var item in ModesMath.Dedupe(_rows.Where(r => r.Box.IsChecked == true).Select(r => r.Captured.Item.Clone())))
        {
            if (mode.Items.Count >= ModesMath.MaxItems)
            {
                break;
            }

            item.Id = ModesMath.NewItemId(mode);
            mode.Items.Add(item);
        }

        if (mode.Items.Count > 0)
        {
            mode.Items[0].Front = true;
        }

        Saved = _runtime.Add(mode);
        DialogResult = true;
    }
}
