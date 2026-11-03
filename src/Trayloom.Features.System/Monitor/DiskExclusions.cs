// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>Quick tools page: the "Excluded drives" list used by "Eject all disks" (spec 02a 5.10, key <c>diskEjectExcludedVolumes</c>).</summary>
public sealed class DiskExclusionsSection : ISettingsSection
{
    public string? FeatureId => "monitor-disk";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "diskExclusions";

    public string? Title => Loc.T("Excluded drives");

    public int Order => 900;

    public IReadOnlyList<string> Keywords => ["eject all disks", "excluded drives", "add drive", "other drive name"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new SettingsCard();
        var host = new StackPanel();
        var rows = new StackPanel();
        var addHost = new ContentControl { Margin = new Thickness(0, 6, 0, 0) };
        var count = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        count.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");

        void Save(List<string> list)
        {
            app.Set("diskEjectExcludedVolumes", list);
            Render();
        }

        void Render()
        {
            var list = app.Get<List<string>>("diskEjectExcludedVolumes");
            count.Text = list.Count == 0 ? string.Empty : list.Count.ToString();
            rows.Children.Clear();
            foreach (var name in list)
            {
                var entry = name;
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(UiKit.Icon(Gl.Usb, 11, ThemeKeys.TextSecondary));
                var label = UiKit.Text(entry, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var remove = Bits.FlatIcon(Gl.CloseCircle, Loc.T("Remove"), () => Save(app.Get<List<string>>("diskEjectExcludedVolumes").Where(x => !string.Equals(x, entry, StringComparison.OrdinalIgnoreCase)).ToList()), 12, 22);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                rows.Children.Add(row);
            }

            ShowAdd(false);
        }

        void ShowAdd(bool custom)
        {
            var list = app.Get<List<string>>("diskEjectExcludedVolumes");
            if (!custom)
            {
                var candidates = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Select(d => string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : d.VolumeLabel)
                    .Where(n => !list.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var button = UiKit.Button(Loc.T("Add drive…"), ButtonKind.Bordered, 12, Gl.Add, null);
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Click += (_, _) =>
                {
                    if (candidates.Count == 0)
                    {
                        ShowAdd(true);
                        return;
                    }

                    var menu = new ContextMenu();
                    foreach (var name in candidates)
                    {
                        var n = name;
                        var item = new MenuItem { Header = n };
                        item.Click += (_, _) => Save([.. app.Get<List<string>>("diskEjectExcludedVolumes"), n]);
                        menu.Items.Add(item);
                    }

                    menu.Items.Add(new Separator());
                    var other = new MenuItem { Header = Loc.T("Other drive name…") };
                    other.Click += (_, _) => ShowAdd(true);
                    menu.Items.Add(other);
                    menu.PlacementTarget = button;
                    menu.IsOpen = true;
                };
                addHost.Content = button;
                return;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new TextBox { Width = 200, MinHeight = 26 };
            box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
            var add = UiKit.Button(Loc.T("Add drive…"), ButtonKind.Bordered, 12, null, null);
            add.IsEnabled = false;
            void Commit()
            {
                var text = box.Text.Trim();
                if (text.Length > 0)
                {
                    Save([.. app.Get<List<string>>("diskEjectExcludedVolumes"), text]);
                }
            }

            box.TextChanged += (_, _) => add.IsEnabled = box.Text.Trim().Length > 0;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
            add.Click += (_, _) => Commit();
            var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Plain, 12, null, () => ShowAdd(false));
            box.Margin = new Thickness(0, 0, 8, 0);
            add.Margin = new Thickness(0, 0, 4, 0);
            row.Children.Add(box);
            row.Children.Add(add);
            row.Children.Add(cancel);
            addHost.Content = row;
            box.Loaded += (_, _) => box.Focus();
            box.ToolTip = Loc.T("Drive or volume name");
        }

        var disclosure = Bits.Disclosure(Loc.T("Excluded drives"), () =>
        {
            var body = new StackPanel();
            body.Children.Add(rows);
            body.Children.Add(addHost);
            return body;
        }, expanded: app.Get<List<string>>("diskEjectExcludedVolumes").Count > 0, indent: 0, fontSize: 13, trailing: count);
        host.Children.Add(disclosure);
        Render();
        card.Add(host);
        card.AddCaption(Loc.T("Drives in this list are never unmounted when using Eject all disks."));
        return card;
    }
}
