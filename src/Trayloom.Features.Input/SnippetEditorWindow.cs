// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>The "New snippet" / "Edit snippet" sheet (440 wide): name, trigger, expansion mode, case, folder, quick menu, text.</summary>
internal sealed class SnippetEditorWindow : Window
{
    private static readonly Regex DateTokenRegex = new(@"\{\{(datetime|date|time)(?:-tz\(([^)}]*)\))?(?::([^}]*))?\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<SnippetItem> _all;
    private readonly SnippetItem? _existing;
    private readonly TextBox _name = new();
    private readonly TextBox _trigger = new();
    private readonly TextBox _folder = new();
    private readonly TextBox _text = new();
    private readonly MacSwitch _ignoreCase;
    private readonly MacSwitch _showInMenu;
    private readonly ComboBox _expand;
    private readonly TextBlock _validation;
    private readonly Button _save;
    private readonly Button _dateButton;
    private readonly Popup _popup;

    public SnippetEditorWindow(List<SnippetItem> all, SnippetItem? existing)
    {
        _all = all;
        _existing = existing;
        Title = existing is null ? Strings.NewSnippet : Strings.EditSnippet;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        this.SetResourceReference(BackgroundProperty, ThemeKeys.SettingsWindow);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(UiKit.Text(Title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));

        var form = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        root.Children.Add(form);

        StyleBox(_name);
        StyleBox(_trigger);
        StyleBox(_folder);
        _trigger.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        form.Children.Add(Row(Strings.NameLabel, WithPlaceholder(_name, Strings.NamePlaceholder)));
        form.Children.Add(Row(Strings.TriggerLabel, WithPlaceholder(_trigger, Strings.TriggerPlaceholder)));

        _expand = InputUi.Combo<string>([("afterDelimiter", Strings.AfterDelimiter), ("immediate", Strings.RightAway)], existing?.Expansion ?? "afterDelimiter", _ => { }, 220);
        _expand.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(Row(Strings.ExpandLabel, _expand));

        _ignoreCase = new MacSwitch(existing?.IgnoresCase ?? false, SwitchSize.Regular);
        _ignoreCase.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(Row(Strings.IgnoreCapitalization, _ignoreCase));

        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.Children.Add(WithPlaceholder(_folder, Strings.FolderPlaceholder));
        var folders = SnippetStore.Folders(all);
        if (folders.Count > 0)
        {
            var menuButton = new Button { Content = UiKit.Icon("", 13, ThemeKeys.TextPrimary), Width = 30, Height = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = Strings.FolderLabel };
            menuButton.SetResourceReference(StyleProperty, ThemeKeys.ButtonBordered);
            menuButton.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = menuButton, Placement = PlacementMode.Bottom };
                foreach (var folder in folders)
                {
                    var item = new MenuItem { Header = folder };
                    item.Click += (_, _) => _folder.Text = folder;
                    menu.Items.Add(item);
                }

                menu.IsOpen = true;
            };
            Grid.SetColumn(menuButton, 1);
            folderRow.Children.Add(menuButton);
        }

        form.Children.Add(Row(Strings.FolderLabel, folderRow));

        _showInMenu = new MacSwitch(existing?.ShowsInLibrary ?? true, SwitchSize.Regular);
        _showInMenu.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(Row(Strings.ShowInQuickMenu, _showInMenu));

        // Text block
        var textHeader = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        textHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        textHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        textHeader.Children.Add(UiKit.Text(Strings.TextLabel, 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        _dateButton = UiKit.Button(Strings.InsertDateTime, ButtonKind.Bordered, 11.5, "", OpenDatePopup);
        Grid.SetColumn(_dateButton, 1);
        textHeader.Children.Add(_dateButton);
        form.Children.Add(textHeader);

        StyleBox(_text);
        _text.AcceptsReturn = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.MinHeight = 90;
        _text.MaxHeight = 200;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        form.Children.Add(WithPlaceholder(_text, Strings.TextPlaceholder));
        var variables = InputUi.Caption(Strings.VariablesHint);
        variables.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(variables);
        var format = InputUi.Caption(Strings.EditorFormatCaption);
        format.Margin = new Thickness(0, 4, 0, 0);
        form.Children.Add(format);

        _validation = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        _validation.Margin = new Thickness(0, 10, 0, 0);
        form.Children.Add(_validation);

        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (existing is not null)
        {
            var delete = UiKit.Button(Strings.Delete, ButtonKind.Destructive, 12, null, () =>
            {
                Deleted = true;
                DialogResult = true;
            });
            buttons.Children.Add(delete);
        }

        var cancel = UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 12, null, () => DialogResult = false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(cancel, 2);
        buttons.Children.Add(cancel);
        _save = UiKit.Button(Strings.Save, ButtonKind.Prominent, 12, null, Commit);
        Grid.SetColumn(_save, 3);
        buttons.Children.Add(_save);
        root.Children.Add(buttons);

        _popup = new Popup { PlacementTarget = _dateButton, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        root.Children.Add(_popup);
        Content = root;

        _name.Text = existing?.Name ?? string.Empty;
        _trigger.Text = existing?.Trigger ?? string.Empty;
        _folder.Text = existing?.Folder ?? string.Empty;
        _text.Text = existing?.Replacement ?? string.Empty;
        _trigger.TextChanged += (_, _) => Validate();
        _text.TextChanged += (_, _) => Validate();
        _text.SelectionChanged += (_, _) => _dateButton.Content = DateButtonContent(TokenAtCaret() is not null);
        _dateButton.Content = DateButtonContent(false);
        Validate();
        Loaded += (_, _) => (existing is null ? _name : _text).Focus();
    }

    /// <summary>The edited / new snippet when the sheet was saved.</summary>
    public SnippetItem? Result { get; private set; }

    /// <summary>True when "Delete" was pressed.</summary>
    public bool Deleted { get; private set; }

    private static void StyleBox(TextBox box) => box.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);

    private static FrameworkElement Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static FrameworkElement WithPlaceholder(TextBox box, string placeholder)
    {
        var grid = new Grid();
        grid.Children.Add(box);
        var hint = UiKit.Text(placeholder, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, box.AcceptsReturn ? 7 : 0, 0, 0);
        hint.VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center;
        grid.Children.Add(hint);
        void Update() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
        return grid;
    }

    private string Sanitized => SnippetStore.SanitizeTrigger(_trigger.Text);

    private void Validate()
    {
        var trigger = Sanitized;
        string? message = null;
        var ignoresCase = _ignoreCase.IsOn;
        if (trigger.Length > 0 && trigger.Length < SnippetStore.MinTrigger)
        {
            message = Strings.TriggerTooShort;
        }
        else if (trigger.Length >= SnippetStore.MinTrigger && SnippetStore.IsDuplicate(_all, trigger, ignoresCase, _existing?.Id))
        {
            message = Strings.DuplicateTrigger;
        }

        _validation.Text = message ?? string.Empty;
        _validation.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        _save.IsEnabled = message is null && trigger.Length >= SnippetStore.MinTrigger && _text.Text.Length > 0;
    }

    private void Commit()
    {
        Validate();
        if (!_save.IsEnabled)
        {
            return;
        }

        var expansion = _expand.SelectedIndex == 1 ? "immediate" : "afterDelimiter";
        var item = _existing?.Clone() ?? new SnippetItem();
        item.Name = _name.Text.Trim();
        item.Trigger = Sanitized;
        item.Replacement = _text.Text.Replace("\r\n", "\n");
        item.Expansion = expansion;
        item.IgnoresCase = _ignoreCase.IsOn;
        item.Folder = _folder.Text.Trim();
        item.ShowsInLibrary = _showInMenu.IsOn;
        Result = item;
        DialogResult = true;
    }

    // ------------------------------------------------------------------------------------------ date / time

    private static string DateButtonContent(bool editing) => editing ? Strings.EditDateTime : Strings.InsertDateTime;

    private Match? TokenAtCaret()
    {
        var caret = _text.CaretIndex;
        var selection = _text.SelectionLength;
        foreach (Match match in DateTokenRegex.Matches(_text.Text))
        {
            var end = match.Index + match.Length;
            if ((caret > match.Index && caret < end) || (selection > 0 && _text.SelectionStart == match.Index && _text.SelectionLength == match.Length))
            {
                return match;
            }
        }

        return null;
    }

    private void OpenDatePopup()
    {
        var token = TokenAtCaret();
        var builder = new DateVariableBuilder(token);
        builder.Done += result =>
        {
            _popup.IsOpen = false;
            if (result is null)
            {
                _text.Focus();
                return;
            }

            if (token is not null)
            {
                _text.Select(token.Index, token.Length);
            }

            _text.SelectedText = result;
            _text.CaretIndex = Math.Min(_text.Text.Length, (token?.Index ?? _text.SelectionStart) + result.Length);
            _text.SelectionLength = 0;
            _text.Focus();
        };
        _popup.Child = builder;
        _popup.IsOpen = true;
    }
}

/// <summary>The date/time builder popover (340 wide): type, style, timezone, custom pattern, live preview.</summary>
internal sealed class DateVariableBuilder : Border
{
    private static readonly string[] Kinds = ["date", "time", "datetime"];
    private static readonly string[] Styles = ["short", "medium", "long", "full", "iso", "custom"];

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["utc"] = "UTC", ["gmt"] = "Europe/London", ["pst"] = "America/Los_Angeles", ["pdt"] = "America/Los_Angeles",
        ["mst"] = "America/Denver", ["mdt"] = "America/Denver", ["cst"] = "America/Chicago", ["cdt"] = "America/Chicago",
        ["est"] = "America/New_York", ["edt"] = "America/New_York", ["cet"] = "Europe/Paris", ["cest"] = "Europe/Paris",
        ["eet"] = "Europe/Athens", ["ist"] = "Asia/Kolkata", ["jst"] = "Asia/Tokyo", ["aest"] = "Australia/Sydney",
        ["bst"] = "Europe/London", ["msk"] = "Europe/Moscow", ["hst"] = "Pacific/Honolulu", ["akst"] = "America/Anchorage",
    };

    private static List<string>? _zoneCache;

    private readonly SegmentedControl _kind;
    private readonly ComboBox _style;
    private readonly TextBlock _namedNote;
    private readonly TextBlock _zoneLabel;
    private readonly Button _clearZone;
    private readonly TextBox _zoneSearch = new();
    private readonly TextBlock _zoneState;
    private readonly ListBox _zoneList = new();
    private readonly TextBox _pattern = new();
    private readonly StackPanel _patternPanel = new();
    private readonly TextBlock _preview;
    private readonly Button _insert;
    private string? _zone;
    private bool _editing;

    public DateVariableBuilder(Match? existing)
    {
        _editing = existing is not null;
        Width = 340;
        Padding = new Thickness(16);
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        Margin = new Thickness(10);
        this.SetResourceReference(BackgroundProperty, ThemeKeys.PanelBase);
        this.SetResourceReference(BorderBrushProperty, ThemeKeys.CardBorder);
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.3 };

        var stack = new StackPanel();
        Child = stack;

        _kind = new SegmentedControl([Strings.DateKind, Strings.TimeKind, Strings.DateTimeKind], 2, stretch: true, fontSize: 12);
        stack.Children.Add(Labeled(Strings.TypeLabel, _kind));

        _style = new ComboBox { ItemsSource = new[] { Strings.StyleShort, Strings.StyleMedium, Strings.StyleLong, Strings.StyleFull, Strings.StyleIso, Strings.StyleCustom }, SelectedIndex = 4, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Spaced(Labeled(Strings.StyleLabel, _style), 10));
        _namedNote = InputUi.Caption(Strings.NamedStyleNote);
        stack.Children.Add(Spaced(_namedNote, 6));

        // timezone block
        var zone = new StackPanel();
        zone.Children.Add(UiKit.Text(Strings.TimezoneLabel, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        var zoneRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        zoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        zoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _zoneLabel = UiKit.Text(Strings.DeviceDefault, 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        zoneRow.Children.Add(_zoneLabel);
        _clearZone = new Button { Content = UiKit.Icon("", 13, ThemeKeys.TextSecondary), ToolTip = Strings.ClearTimezone, Padding = new Thickness(2), Visibility = Visibility.Collapsed };
        _clearZone.SetResourceReference(StyleProperty, ThemeKeys.ButtonPlain);
        _clearZone.Click += (_, _) => SetZone(null);
        Grid.SetColumn(_clearZone, 1);
        zoneRow.Children.Add(_clearZone);
        zone.Children.Add(zoneRow);

        _zoneSearch.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _zoneSearch.Margin = new Thickness(0, 6, 0, 0);
        _zoneSearch.ToolTip = Strings.SearchTimezones;
        var searchGrid = new Grid();
        searchGrid.Children.Add(_zoneSearch);
        var hint = UiKit.Text(Strings.SearchTimezones, 12.5, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 6, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        searchGrid.Children.Add(hint);
        _zoneState = UiKit.Icon(string.Empty, 13, ThemeKeys.Green, FontWeights.SemiBold);
        _zoneState.HorizontalAlignment = HorizontalAlignment.Right;
        _zoneState.Margin = new Thickness(0, 6, 10, 0);
        _zoneState.IsHitTestVisible = false;
        searchGrid.Children.Add(_zoneState);
        zone.Children.Add(searchGrid);

        _zoneList.MaxHeight = 140;
        _zoneList.Margin = new Thickness(0, 4, 0, 0);
        _zoneList.BorderThickness = new Thickness(0);
        _zoneList.Background = Brushes.Transparent;
        _zoneList.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _zoneList.FontSize = 11.5;
        _zoneList.Visibility = Visibility.Collapsed;
        _zoneList.SelectionChanged += (_, _) =>
        {
            if (_zoneList.SelectedItem is string id)
            {
                SetZone(id);
            }
        };
        zone.Children.Add(_zoneList);
        stack.Children.Add(Spaced(zone, 10));

        _pattern.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _pattern.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _pattern.Text = "yyyy-MM-dd";
        _patternPanel.Children.Add(UiKit.Text(Strings.PatternLabel, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        _pattern.Margin = new Thickness(0, 4, 0, 0);
        _patternPanel.Children.Add(_pattern);
        stack.Children.Add(Spaced(_patternPanel, 10));

        var previewRow = new Grid();
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewRow.Children.Add(UiKit.Text(Strings.PreviewLabel, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        _preview = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        _preview.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _preview.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_preview, 1);
        previewRow.Children.Add(_preview);
        stack.Children.Add(Spaced(previewRow, 10));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 12, null, () => Done?.Invoke(null));
        _insert = UiKit.Button(_editing ? Strings.Update : Strings.Insert, ButtonKind.Prominent, 12, null, () => Done?.Invoke(BuildToken()));
        _insert.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_insert);
        stack.Children.Add(Spaced(buttons, 12));

        // initial state from an existing token
        var kindIndex = 2;
        var styleIndex = 4;
        if (existing is not null)
        {
            kindIndex = Math.Max(0, Array.IndexOf(Kinds, existing.Groups[1].Value.ToLowerInvariant()));
            if (existing.Groups[2].Success)
            {
                _zone = existing.Groups[2].Value;
            }

            if (existing.Groups[3].Success)
            {
                var pattern = existing.Groups[3].Value;
                styleIndex = 5;
                for (var i = 0; i < 5; i++)
                {
                    if (SnippetVariables.NamedPattern(Kinds[kindIndex], Styles[i], CultureInfo.CurrentCulture) == pattern)
                    {
                        styleIndex = i;
                        break;
                    }
                }

                _pattern.Text = pattern;
            }
        }

        _kind.SelectedIndex = kindIndex;
        _style.SelectedIndex = styleIndex;
        if (_zone is not null)
        {
            SetZone(_zone);
        }

        _kind.SelectionChanged += _ => Refresh();
        _style.SelectionChanged += (_, _) => Refresh();
        _pattern.TextChanged += (_, _) => Refresh();
        _zoneSearch.TextChanged += (_, _) =>
        {
            hint.Visibility = _zoneSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSearch();
        };
        _zoneSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var matches = Search(_zoneSearch.Text);
                if (matches.Count == 1)
                {
                    SetZone(matches[0]);
                }

                e.Handled = true;
            }
        };
        Refresh();
    }

    /// <summary>Raised with the token to write, or null when cancelled.</summary>
    public event Action<string?>? Done;

    private static FrameworkElement Labeled(string label, UIElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(label, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        if (control is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 4, 0, 0);
        }

        stack.Children.Add(control);
        return stack;
    }

    private static T Spaced<T>(T element, double top) where T : FrameworkElement
    {
        element.Margin = new Thickness(element.Margin.Left, element.Margin.Top + top, element.Margin.Right, element.Margin.Bottom);
        return element;
    }

    private string Kind => Kinds[Math.Clamp(_kind.SelectedIndex, 0, 2)];

    private string StyleName => Styles[Math.Clamp(_style.SelectedIndex, 0, 5)];

    private string CurrentPattern => StyleName == "custom" ? _pattern.Text : SnippetVariables.NamedPattern(Kind, StyleName, CultureInfo.CurrentCulture);

    private void Refresh()
    {
        var custom = StyleName == "custom";
        _patternPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _namedNote.Visibility = StyleName is "short" or "medium" or "long" or "full" ? Visibility.Visible : Visibility.Collapsed;
        var pattern = CurrentPattern;
        _insert.IsEnabled = !(custom && pattern.Length == 0);
        try
        {
            var zone = _zone is null ? null : SnippetVariables.FindZone(_zone);
            var moment = zone is null ? DateTimeOffset.Now : TimeZoneInfo.ConvertTime(DateTimeOffset.Now, zone);
            _preview.Text = IcuDate.Format(moment, pattern, CultureInfo.CurrentCulture, zone ?? TimeZoneInfo.Local);
        }
        catch
        {
            _preview.Text = string.Empty;
        }
    }

    private string BuildToken()
    {
        var pattern = CurrentPattern;
        var tz = _zone is null ? string.Empty : $"-tz({_zone})";
        return "{{" + Kind + tz + ":" + pattern + "}}";
    }

    private void SetZone(string? id)
    {
        _zone = id;
        _zoneLabel.Text = id ?? Strings.DeviceDefault;
        _zoneLabel.FontFamily = id is null ? UiKit.TextFont : new FontFamily("Cascadia Mono, Consolas");
        _clearZone.Visibility = id is null ? Visibility.Collapsed : Visibility.Visible;
        _zoneList.Visibility = Visibility.Collapsed;
        _zoneState.Text = string.Empty;
        if (_zoneSearch.Text.Length > 0)
        {
            _zoneSearch.Text = string.Empty;
        }

        Refresh();
    }

    private static string Norm(string text) => Regex.Replace(text.ToLowerInvariant(), "[\\s_/\\-]+", " ").Trim();

    private static List<string> AllZones()
    {
        if (_zoneCache is not null)
        {
            return _zoneCache;
        }

        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "UTC" };
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var iana))
            {
                set.Add(iana);
            }
        }

        foreach (var extra in new[]
        {
            "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Phoenix", "America/Anchorage", "America/Toronto",
            "America/Mexico_City", "America/Sao_Paulo", "America/Argentina/Buenos_Aires", "America/Bogota", "Europe/London", "Europe/Dublin", "Europe/Paris",
            "Europe/Berlin", "Europe/Madrid", "Europe/Rome", "Europe/Amsterdam", "Europe/Stockholm", "Europe/Athens", "Europe/Istanbul", "Europe/Moscow",
            "Africa/Cairo", "Africa/Lagos", "Africa/Johannesburg", "Asia/Dubai", "Asia/Karachi", "Asia/Kolkata", "Asia/Dhaka", "Asia/Bangkok", "Asia/Singapore",
            "Asia/Hong_Kong", "Asia/Shanghai", "Asia/Seoul", "Asia/Tokyo", "Australia/Perth", "Australia/Sydney", "Pacific/Auckland", "Pacific/Honolulu",
        })
        {
            if (SnippetVariables.FindZone(extra) is not null)
            {
                set.Add(extra);
            }
        }

        _zoneCache = [.. set];
        return _zoneCache;
    }

    private static List<string> Search(string query)
    {
        var q = Norm(query);
        if (q.Length == 0)
        {
            return [];
        }

        var zones = AllZones();
        var result = new List<(string Id, int Rank)>();
        if (Abbreviations.TryGetValue(query.Trim(), out var abbreviation))
        {
            result.Add((abbreviation, -1));
        }

        foreach (var id in zones)
        {
            var n = Norm(id);
            var city = n.Contains(' ') ? n[(n.LastIndexOf(' ') + 1)..] : n;
            var rank = n == q ? 0 : city.StartsWith(q) ? 1 : n.StartsWith(q) ? 2 : n.Contains(q) ? 3 : -2;
            if (rank >= 0)
            {
                result.Add((id, rank));
            }
        }

        return [.. result.OrderBy(r => r.Rank).ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private void UpdateSearch()
    {
        var matches = Search(_zoneSearch.Text);
        _zoneList.ItemsSource = matches.Take(200).ToList();
        _zoneList.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _zoneList.MaxHeight = Math.Min(140, matches.Count * 22 + 4);
        var settled = _zoneSearch.Text.Trim().Length > 0 && (matches.Count == 1 || matches.Count == 0);
        if (!settled)
        {
            _zoneState.Text = string.Empty;
            return;
        }

        var valid = matches.Count == 1;
        _zoneState.Text = valid ? "" : "";
        _zoneState.SetResourceReference(TextBlock.ForegroundProperty, valid ? ThemeKeys.Green : ThemeKeys.Red);
        _zoneState.ToolTip = valid ? Strings.ValidTimezone : Strings.UnrecognizedTimezone;
    }
}
