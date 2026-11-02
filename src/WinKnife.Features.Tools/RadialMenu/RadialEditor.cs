// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal enum RadialEditorResult
{
    Cancel,
    Save,
    Remove,
    DrillIn,
}

/// <summary>The action editor sheet (spec 3.8.1): kind, payload, name and icon of one wheel item.</summary>
internal static class RadialEditor
{
    /// <summary>Shows the sheet for <paramref name="item"/> (a working copy edited in place; apply it only on Save / DrillIn).</summary>
    public static RadialEditorResult Show(Window? owner, IContributionServices ctx, RadialItem item, bool isNew, bool rootLevel)
    {
        var window = RadialDialogs.NewDialog(owner, Loc.T("Add action"), 440);
        var result = RadialEditorResult.Cancel;
        var tools = ToolActions.RadialOrder.Where(t => ToolActions.IsAvailable(ctx, t) && (t != "shelf" || ctx.App.Get<bool>("shelfEnabled", false))).ToList();
        var toggles = RadialCatalog.QuickToggles.Where(t => QuickToggleOps.Available(ctx, t)).ToList();
        var kinds = RadialKinds.All.Where(k => k switch
        {
            RadialKinds.Tool => tools.Count > 0,
            RadialKinds.QuickToggle => toggles.Count > 0,
            RadialKinds.WindowLayout => ctx.Runtime.IsAvailable("window-layout"),
            RadialKinds.Submenu => rootLevel,
            _ => true,
        }).ToList();
        if (!kinds.Contains(item.Kind))
        {
            kinds.Add(item.Kind);
        }

        var root = new StackPanel { Margin = new Thickness(18) };
        var heading = UiKit.Text(isNew ? Loc.T("Add action") : RadialModel.DisplayName(item), 14, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        root.Children.Add(heading);

        var form = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        root.Children.Add(form);
        var payloadHost = new ContentControl { Margin = new Thickness(0, 14, 0, 0) };
        var iconCells = new List<(string Symbol, Border Cell)>();
        Border? automaticCell = null;
        var notes = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var save = UiKit.Button(Loc.T("Save"), ButtonKind.Prominent, 12.5);

        void RefreshValidity()
        {
            save.IsEnabled = RadialModel.IsValidPayload(item);
            RefreshNotes();
        }

        void RefreshAutomatic()
        {
            if (automaticCell is not null)
            {
                var auto = item.Clone();
                auto.SymbolName = "";
                automaticCell.Child = RadialWheelWindow.MakeIcon(auto, ThemeManager.IsLight, null, 14, 20);
            }
        }

        void RefreshIcons()
        {
            foreach (var (symbol, cell) in iconCells)
            {
                var selected = symbol == item.SymbolName;
                StyleCell(cell, selected);
            }
        }

        void RefreshNotes()
        {
            notes.Children.Clear();
            if (item.Kind == RadialKinds.Url && item.Payload.Length > 0 && RadialModel.NormalizedUrl(item.Payload) is null)
            {
                notes.Children.Add(UiKit.Text(Loc.T("Enter a valid link."), 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
            }

            if (item.Kind == RadialKinds.Submenu)
            {
                notes.Children.Add(UiKit.Text(Loc.T("Opens a second wheel with its own actions."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
                if (!isNew)
                {
                    var link = UiKit.Button(Loc.T("Edit actions >"), ButtonKind.Plain, 11.5, onClick: () =>
                    {
                        result = RadialEditorResult.DrillIn;
                        window.Close();
                    });
                    link.HorizontalAlignment = HorizontalAlignment.Left;
                    link.Margin = new Thickness(-8, 4, 0, 0);
                    notes.Children.Add(link);
                }
            }
        }

        // ---- kind picker
        var kindCombo = new ComboBox { MinWidth = 180, ItemsSource = kinds.Select(RadialKinds.Label).ToList(), SelectedIndex = Math.Max(0, kinds.IndexOf(item.Kind)) };
        form.Children.Add(Row(Loc.T("Action"), kindCombo));

        void BuildPayload()
        {
            payloadHost.Content = item.Kind switch
            {
                RadialKinds.App => FileRow(Loc.T("Open an app"), item, isApp: true, RefreshValidity, RefreshAutomatic, owner),
                RadialKinds.File => FileRow(Loc.T("Open a file or folder"), item, isApp: false, RefreshValidity, RefreshAutomatic, owner),
                RadialKinds.Url => UrlRow(item, RefreshValidity, RefreshAutomatic, RefreshIcons),
                RadialKinds.Shortcut => ShortcutRow(item, RefreshValidity),
                RadialKinds.Tool => ComboRow(Loc.T("Tool"), tools.Select(ToolActions.TitleOf).ToList(), tools.IndexOf(item.Payload), i =>
                {
                    item.Payload = tools[i];
                    RefreshValidity();
                    RefreshAutomatic();
                }),
                RadialKinds.QuickToggle => ComboRow(Loc.T("Quick toggles"), toggles.Select(RadialCatalog.ToggleTitle).ToList(), toggles.IndexOf(item.Payload), i =>
                {
                    item.Payload = toggles[i];
                    RefreshValidity();
                    RefreshAutomatic();
                }),
                RadialKinds.WindowLayout => ComboRow(Loc.T("Window layout"), RadialCatalog.Layouts.Select(l => l.Title).ToList(), RadialCatalog.Layouts.ToList().FindIndex(l => l.Raw == item.Payload), i =>
                {
                    item.Payload = RadialCatalog.Layouts[i].Raw;
                    RefreshValidity();
                    RefreshAutomatic();
                }),
                RadialKinds.Media => ComboRow(Loc.T("Control"), RadialCatalog.MediaKeys.Select(RadialCatalog.MediaLabel).ToList(), Array.IndexOf(RadialCatalog.MediaKeys, item.Payload), i =>
                {
                    item.Payload = RadialCatalog.MediaKeys[i];
                    RefreshValidity();
                    RefreshAutomatic();
                }),
                _ => null,
            };
            payloadHost.Visibility = item.Kind == RadialKinds.Submenu ? Visibility.Collapsed : Visibility.Visible;
        }

        kindCombo.SelectionChanged += (_, _) =>
        {
            if (kindCombo.SelectedIndex < 0 || kinds[kindCombo.SelectedIndex] == item.Kind)
            {
                return;
            }

            item.Kind = kinds[kindCombo.SelectedIndex];
            item.Payload = item.Kind switch
            {
                RadialKinds.Tool => tools.FirstOrDefault() ?? "",
                RadialKinds.QuickToggle => toggles.FirstOrDefault() ?? "",
                RadialKinds.WindowLayout => "leftHalf",
                RadialKinds.Media => "playPause",
                _ => "",
            };
            item.Children = item.Kind == RadialKinds.Submenu ? item.Children : [];
            if (item.Kind != RadialKinds.Url)
            {
                item.CustomIconData = null;
            }

            BuildPayload();
            RefreshValidity();
            RefreshAutomatic();
        };
        form.Children.Add(payloadHost);

        // ---- name
        var nameBox = RadialDialogs.NewTextBox(item.Name, 200);
        nameBox.TextChanged += (_, _) => item.Name = nameBox.Text;
        var nameRow = Row(Loc.T("Name"), RadialDialogs.WithPlaceholder(nameBox, Loc.T("Automatic")));
        nameRow.Margin = new Thickness(0, 14, 0, 0);
        form.Children.Add(nameRow);

        // ---- icon picker
        var iconBlock = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        iconBlock.Children.Add(UiKit.Text(Loc.T("Icon"), 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        automaticCell = Cell(null, Loc.T("Automatic"), () =>
        {
            item.SymbolName = "";
            RefreshIcons();
            RefreshAutomatic();
        });
        iconCells.Add(("", automaticCell));
        wrap.Children.Add(automaticCell);
        foreach (var (id, glyph) in RadialSymbols.All)
        {
            var symbol = id;
            var cell = Cell(glyph, symbol, () =>
            {
                item.SymbolName = symbol;
                RefreshIcons();
            });
            iconCells.Add((symbol, cell));
            wrap.Children.Add(cell);
        }

        iconBlock.Children.Add(new ScrollViewer { Content = wrap, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        form.Children.Add(iconBlock);
        form.Children.Add(notes);

        // ---- buttons
        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!isNew)
        {
            var remove = UiKit.Button(Loc.T("Remove"), ButtonKind.Plain, 12.5, onClick: () =>
            {
                result = RadialEditorResult.Remove;
                window.Close();
            });
            remove.Foreground = UiKit.BrushOf(ThemeKeys.Red);
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Children.Add(remove);
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, 12.5, onClick: window.Close));
        save.Margin = new Thickness(8, 0, 0, 0);
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            item.Name = item.Name.Trim();
            item.Payload = item.Payload.Trim();
            if (item.Kind == RadialKinds.Url)
            {
                item.Payload = RadialModel.NormalizedUrl(item.Payload) ?? item.Payload;
            }

            result = RadialEditorResult.Save;
            window.Close();
        };
        right.Children.Add(save);
        Grid.SetColumn(right, 1);
        buttons.Children.Add(right);
        root.Children.Add(buttons);
        window.Content = root;

        BuildPayload();
        RefreshIcons();
        RefreshAutomatic();
        RefreshValidity();
        window.ShowDialog();
        return result == RadialEditorResult.DrillIn && !RadialModel.IsValidPayload(item) ? RadialEditorResult.Cancel : result;
    }

    // ------------------------------------------------------------------ rows

    private static FrameworkElement Row(string label, UIElement control) => new SettingsRow(null, label, null, control);

    private static FrameworkElement ComboRow(string label, List<string> titles, int selected, Action<int> changed)
    {
        var combo = new ComboBox { MinWidth = 200, ItemsSource = titles, SelectedIndex = selected };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(combo.SelectedIndex);
            }
        };
        return Row(label, combo);
    }

    private static FrameworkElement FileRow(string label, RadialItem item, bool isApp, Action validity, Action automatic, Window? owner)
    {
        var button = UiKit.Button(item.Payload.Length == 0 ? Loc.T("Choose…") : RadialModel.FileDisplayName(item.Payload), ButtonKind.Bordered, 12.5);
        button.MinWidth = 150;
        void Apply(string path)
        {
            RadialModel.ForgetName(path);
            item.Payload = path;
            button.Content = RadialModel.FileDisplayName(path);
            validity();
            automatic();
        }

        void PickFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = isApp ? "Programs (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files|*.*" : "All files|*.*",
                InitialDirectory = isApp ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (dialog.ShowDialog(owner) == true)
            {
                Apply(dialog.FileName);
            }
        }

        button.Click += (_, _) =>
        {
            if (isApp)
            {
                PickFile();
                return;
            }

            var menu = new ContextMenu { PlacementTarget = button, IsOpen = false };
            var file = new MenuItem { Header = "File…" };
            file.Click += (_, _) => PickFile();
            var folder = new MenuItem { Header = "Folder…" };
            folder.Click += (_, _) =>
            {
                var dialog = new OpenFolderDialog { InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
                if (dialog.ShowDialog(owner) == true)
                {
                    Apply(dialog.FolderName);
                }
            };
            menu.Items.Add(file);
            menu.Items.Add(folder);
            menu.IsOpen = true;
        };
        return Row(label, button);
    }

    private static FrameworkElement UrlRow(RadialItem item, Action validity, Action automatic, Action icons)
    {
        var stack = new StackPanel();
        var box = RadialDialogs.NewTextBox(item.Payload, 220);
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 11.5 };
        var fetch = UiKit.Button(Loc.T("Fetch Website Icon"), ButtonKind.Bordered, 12);
        var running = false;

        void ShowStatus(string? text, string brushKey, string? glyph = null)
        {
            status.Inlines.Clear();
            if (text is null)
            {
                status.Visibility = Visibility.Collapsed;
                return;
            }

            status.Visibility = Visibility.Visible;
            if (glyph is not null)
            {
                var g = new System.Windows.Documents.Run(glyph + " ") { FontFamily = UiKit.IconFont };
                g.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brushKey);
                status.Inlines.Add(g);
            }

            var run = new System.Windows.Documents.Run(text);
            run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brushKey);
            status.Inlines.Add(run);
        }

        void RefreshFetch() => fetch.IsEnabled = !running && item.Payload.Length > 0 && RadialModel.NormalizedUrl(item.Payload) is not null;

        box.TextChanged += (_, _) =>
        {
            item.Payload = box.Text;
            ShowStatus(null, ThemeKeys.TextSecondary);
            RefreshFetch();
            validity();
            automatic();
        };
        fetch.Click += async (_, _) =>
        {
            if (RadialModel.NormalizedUrl(item.Payload) is not { } url)
            {
                return;
            }

            running = true;
            RefreshFetch();
            ShowStatus(Loc.T("Fetching icon…"), ThemeKeys.TextSecondary);
            var data = await RadialFavicon.FetchAsync(url);
            running = false;
            if (data is null)
            {
                ShowStatus(Loc.T("Could not find a website icon"), ThemeKeys.Orange, "");
            }
            else
            {
                item.CustomIconData = data;
                item.SymbolName = "";
                ShowStatus(Loc.T("Icon downloaded"), ThemeKeys.Green, "");
                icons();
                automatic();
            }

            RefreshFetch();
        };
        stack.Children.Add(Row(Loc.T("Open a link"), RadialDialogs.WithPlaceholder(box, "example.com")));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        line.Children.Add(fetch);
        stack.Children.Add(line);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        stack.Children.Add(status);
        var disclaimer = UiKit.Text(Loc.T("Connects to the website once to download its icon. Saved locally."), 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        disclaimer.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(disclaimer);
        ShowStatus(null, ThemeKeys.TextSecondary);
        RefreshFetch();
        return stack;
    }

    private static FrameworkElement ShortcutRow(RadialItem item, Action validity)
    {
        var recorder = new HotkeyRecorder { Width = 108, Hotkey = Hotkey.TryParse(item.Payload, out var hk) ? hk : null };
        recorder.HotkeyChanged += (_, h) =>
        {
            item.Payload = h?.ToString() ?? "";
            validity();
        };
        return Row(Loc.T("Press a shortcut"), recorder);
    }

    // ------------------------------------------------------------------ icon cells

    private static Border Cell(string? glyph, string tooltip, Action onClick)
    {
        var cell = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 6, 6), ToolTip = tooltip, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(1) };
        if (glyph is not null)
        {
            var text = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
            cell.Child = text;
        }

        cell.MouseLeftButtonUp += (_, _) => onClick();
        StyleCell(cell, false);
        return cell;
    }

    private static void StyleCell(Border cell, bool selected)
    {
        var accent = ThemeManager.AccentColor;
        if (selected)
        {
            cell.Background = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
            cell.BorderBrush = new SolidColorBrush(Color.FromArgb(191, accent.R, accent.G, accent.B));
        }
        else
        {
            cell.Background = new SolidColorBrush(Color.FromArgb(15, 128, 128, 128));
            cell.BorderBrush = new SolidColorBrush(Color.FromArgb(33, 128, 128, 128));
        }
    }
}
