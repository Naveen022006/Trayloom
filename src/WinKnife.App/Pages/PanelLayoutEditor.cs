// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>The "Tray &amp; panel" page: tab order / visibility editor with a miniature, tray icon gallery, "Show tray icon".</summary>
internal static class PanelLayoutEditor
{
    private static readonly string[] IconGallery =
    [
        "E945", "E735", "EB51", "ECAD", "E794", "EC0A", "EB42", "E708", "E706", "E753", "E7C8", "E7F8", "E950", "E964", "EC4A", "E765", "E9D9", "E90F", "E8D7", "E8D6",
    ];

    public static FrameworkElement Build(ShellServices s, Action rebuild)
    {
        var card = new SettingsCard(Loc.T("Tray & panel"));
        card.AddCaption(Loc.T("Click WinKnife’s icon in the system tray to open the panel. Its tabs appear in this order."));

        var editable = PanelLayout.Editable(s.Runtime, s.App);
        var selected = editable.FirstOrDefault(t => PanelLayout.IsShown(s.App, t))?.Id ?? editable.FirstOrDefault()?.Id;
        var miniatureHost = new Border();
        var rowsHost = new StackPanel();

        void RefreshMiniature(string? tabId)
        {
            miniatureHost.Child = Miniature(s, tabId ?? selected);
        }

        void BuildRows()
        {
            rowsHost.Children.Clear();
            var tabs = PanelLayout.Editable(s.Runtime, s.App);
            var shownCount = tabs.Count(t => PanelLayout.IsShown(s.App, t));
            foreach (var tab in tabs)
            {
                var shown = PanelLayout.IsShown(s.App, tab);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var handle = new Border { Background = Brushes.Transparent, Child = UiKit.Icon(Glyphs.Handle, 11, ThemeKeys.TextTertiary, FontWeights.SemiBold), Width = 16 };
                grid.Children.Add(handle);
                var tile = UiKit.IconTile(tab.Glyph, 26, 12, 7, shown ? ThemeKeys.Accent : ThemeKeys.TextSecondary, shown ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
                Grid.SetColumn(tile, 2);
                grid.Children.Add(tile);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                text.Children.Add(UiKit.Text(tab.Title, 13, FontWeights.Medium, shown ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary));
                text.Children.Add(UiKit.Text(tab.Description, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
                Grid.SetColumn(text, 4);
                grid.Children.Add(text);
                var toggle = new MacSwitch(shown, SwitchSize.Small) { IsEnabled = !(shown && shownCount <= 1) };
                var captured = tab;
                toggle.Toggled += on =>
                {
                    s.App.Set(AppKeys.PanelShow(captured.Id), on);
                    BuildRows();
                    RefreshMiniature(captured.Id);
                };
                Grid.SetColumn(toggle, 5);
                grid.Children.Add(toggle);

                var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = grid, Tag = tab.Id, Background = Brushes.Transparent };
                row.MouseEnter += (_, _) =>
                {
                    row.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
                    RefreshMiniature(captured.Id);
                };
                row.MouseLeave += (_, _) =>
                {
                    row.Background = Brushes.Transparent;
                    RefreshMiniature(selected);
                };
                row.MouseLeftButtonUp += (_, _) =>
                {
                    selected = captured.Id;
                    RefreshMiniature(selected);
                };
                DragReorder.Attach(rowsHost, row, handle, rows =>
                {
                    var ids = rows.Select(r => (string)r.Tag).ToList();
                    var all = PanelLayout.Ordered(s.App).Select(t => t.Id).Where(id => !ids.Contains(id)).ToList();
                    // Keep unavailable tabs where they were: insert the edited block in front of them in saved order.
                    PanelLayout.SaveOrder(s.App, ids.Concat(all));
                    RefreshMiniature(selected);
                });
                rowsHost.Children.Add(row);
            }
        }

        BuildRows();
        RefreshMiniature(selected);

        var editor = new Grid();
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Width = 232, Margin = new Thickness(0, 0, 20, 0) };
        left.Children.Add(miniatureHost);
        var hint = UiKit.Text(Loc.T("Drag to reorder. Switch off anything you don’t need."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        hint.Margin = new Thickness(0, 10, 0, 0);
        left.Children.Add(hint);
        editor.Children.Add(left);
        Grid.SetColumn(rowsHost, 1);
        editor.Children.Add(rowsHost);
        card.Add(editor);

        card.AddDivider();
        card.Add(IconRow(s, rebuild));
        card.AddDivider();
        var show = UiKit.Button(Loc.T("Show tray icon"), ButtonKind.Bordered, 13, onClick: () => s.Shell.ReshowTray());
        card.Add(new SettingsRow("", Loc.T("Can’t find the icon?"), Loc.T("A crowded system tray can hide it; drag it out of the overflow (^)."), show));
        return card;
    }

    private static UIElement Miniature(ShellServices s, string? activeTab)
    {
        var tabs = PanelLayout.Editable(s.Runtime, s.App).Where(t => PanelLayout.IsShown(s.App, t)).ToList();
        var outer = new StackPanel();
        var panel = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8), BorderThickness = new Thickness(0.7) };
        panel.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        panel.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        var col = new StackPanel();
        var mark = BrandMark.Create(32, ThemeKeys.TextPrimary);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        col.Children.Add(mark);

        var strip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0.7) };
        strip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        strip.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        var tabRow = new Grid();
        for (var i = 0; i < tabs.Count; i++)
        {
            tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var active = tabs[i].Id == activeTab;
            var cell = new Border { Height = 20, CornerRadius = new CornerRadius(5), Child = UiKit.Icon(tabs[i].Glyph, 9, active ? ThemeKeys.Accent : ThemeKeys.TabIdle, FontWeights.SemiBold), ToolTip = tabs[i].Title };
            if (active)
            {
                cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TabSelectedFill);
            }

            Grid.SetColumn(cell, i);
            tabRow.Children.Add(cell);
        }

        strip.Child = tabRow;
        col.Children.Add(strip);

        var open = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0.7) };
        open.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        open.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        var body = new StackPanel();
        var activeInfo = PanelTabs.Get(activeTab ?? string.Empty);
        body.Children.Add(UiKit.Text((activeInfo?.Title ?? "Panel").ToUpperInvariant(), 8, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        foreach (var width in new[] { 200.0, 124.0, 160.0 })
        {
            var cap = new Border { Height = 5, Width = width, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
            cap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Track);
            body.Children.Add(cap);
        }

        open.Child = body;
        col.Children.Add(open);

        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var (i, glyph) in new[] { (0, Glyphs.Settings), (2, Glyphs.Power) })
        {
            var pill = new Border { Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(0.7), Child = UiKit.Icon(glyph, 9, ThemeKeys.TextSecondary) };
            pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
            pill.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            Grid.SetColumn(pill, i);
            footer.Children.Add(pill);
        }

        col.Children.Add(footer);
        panel.Child = col;
        outer.Children.Add(panel);

        // Taskbar strip under the panel (the tray lives at the bottom on Windows).
        var bar = new Border { Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0xD1, 0, 0, 0)) };
        var items = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        items.Children.Add(MiniIcon("", 0.5));
        items.Children.Add(MiniIcon("", 0.5));
        var glyphHost = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 2, 3, 2), Background = new SolidColorBrush(Color.FromArgb(0x29, 255, 255, 255)), Margin = new Thickness(10, 0, 0, 0), Child = BrandMark.Create(20, ThemeKeys.OnAccent) };
        items.Children.Add(glyphHost);
        bar.Child = items;
        outer.Children.Add(bar);
        return outer;
    }

    private static UIElement MiniIcon(string glyph, double opacity)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 10, Foreground = Brushes.White, Opacity = opacity, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        return icon;
    }

    private static UIElement IconRow(ShellServices s, Action rebuild)
    {
        var current = s.App.Get<string>(AppKeys.MenuBarIconSymbol);
        var stack = new StackPanel();
        stack.Children.Add(new SettingsRow(null, Loc.T("Tray icon"), Loc.T("Choose the icon WinKnife shows in the system tray.")));
        var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        wrap.Children.Add(Cell(BrandMark.Create(18, ThemeKeys.TextPrimary), Loc.T("Use the WinKnife icon"), current.Length == 0, () => Set(string.Empty)));
        foreach (var code in IconGallery)
        {
            var glyph = char.ConvertFromUtf32(Convert.ToInt32(code, 16));
            wrap.Children.Add(Cell(UiKit.Icon(glyph, 14, ThemeKeys.TextPrimary, FontWeights.SemiBold), code, string.Equals(current, code, StringComparison.OrdinalIgnoreCase), () => Set(code)));
        }

        stack.Children.Add(wrap);
        var hint = UiKit.Text(Loc.T("Pick one of the glyphs, or type the hex code of any Segoe Fluent Icons glyph in settings.json (menuBarIconSymbol)."), 11, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true);
        hint.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(hint);
        return stack;

        void Set(string code)
        {
            s.App.Set(AppKeys.MenuBarIconSymbol, code);
            rebuild();
        }
    }

    private static UIElement Cell(UIElement glyph, string tooltip, bool selected, Action onClick)
    {
        var cell = new Border
        {
            Width = 34,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 6, 6),
            Child = glyph,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(selected ? 1.2 : 1),
        };
        cell.SetResourceReference(Border.BackgroundProperty, selected ? ThemeKeys.AccentFill18 : ThemeKeys.ControlFill);
        cell.SetResourceReference(Border.BorderBrushProperty, selected ? ThemeKeys.Accent : ThemeKeys.Divider);
        cell.MouseLeftButtonUp += (_, _) => onClick();
        return cell;
    }
}
