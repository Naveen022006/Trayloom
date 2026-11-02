// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Settings > Quick panel > "Quick toggles" card (dark mode button, keyboard light, excluded drives, caption).</summary>
public sealed class QuickTogglesSettingsSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "quick-toggles";

    /// <inheritdoc />
    public string PageId => SettingsPages.QuickTools;

    /// <inheritdoc />
    public string? Anchor => "quickToggles";

    /// <inheritdoc />
    public string? Title => Loc.T("Quick toggles");

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["dark mode", "keyboard light", "excluded drives", "eject", "recycle bin", "hidden files", "desktop icons"];

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext context)
    {
        context.App.Register("diskEjectExcludedVolumes", new List<string>());
        var card = new SettingsCard(Loc.T("Quick toggles"));

        // Dark mode button: title and glyph follow the live scheme.
        var host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left };
        void BuildButton()
        {
            host.Content = UiKit.Button(QuickToggleOps.Title(context, "darkMode"), ButtonKind.Bordered, 12.5, QuickToggleOps.Glyph("darkMode"), () =>
            {
                _ = QuickToggleOps.RunAsync(context, "darkMode");
                Plat.After(context.Ui, 300, BuildButton);
            });
        }

        BuildButton();
        card.Add(host);

        // Keyboard light: only when a controllable keyboard exists.
        if (ToggleKeyboardLight.Present is null)
        {
            void OnProbed()
            {
                ToggleKeyboardLight.Probed -= OnProbed;
                if (ToggleKeyboardLight.Present == true)
                {
                    context.Ui.BeginInvoke(context.RequestRebuild);
                }
            }

            ToggleKeyboardLight.Probed += OnProbed;
            _ = ToggleKeyboardLight.ProbeAsync();
        }
        else if (ToggleKeyboardLight.Present == true)
        {
            var lastLevel = 1.0;
            var level = ToggleKeyboardLight.Level;
            var percent = UiKit.Text($"{Math.Round(level * 100)}%", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            percent.Width = 34;
            percent.TextAlignment = TextAlignment.Right;
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = level, Width = 190, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Keyboard light") };
            var toggle = new MacSwitch(level > 0, SwitchSize.Regular);
            var updating = false;
            toggle.Toggled += on =>
            {
                if (updating)
                {
                    return;
                }

                updating = true;
                ToggleKeyboardLight.SetLevel(on ? lastLevel : 0);
                slider.Value = on ? lastLevel : 0;
                percent.Text = $"{Math.Round(slider.Value * 100)}%";
                updating = false;
            };
            slider.ValueChanged += (_, e) =>
            {
                if (updating)
                {
                    return;
                }

                updating = true;
                if (e.NewValue > 0)
                {
                    lastLevel = e.NewValue;
                }

                ToggleKeyboardLight.SetLevel(e.NewValue);
                toggle.IsOn = e.NewValue > 0;
                percent.Text = $"{Math.Round(e.NewValue * 100)}%";
                updating = false;
            };
            card.Add(new SettingsRow("", Loc.T("Keyboard light"), null, toggle));
            var sliderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
            sliderRow.Children.Add(slider);
            sliderRow.Children.Add(percent);
            card.Add(sliderRow);
        }

        card.Add(new DiskExclusionsList(context));
        card.AddCaption(Loc.T("One-click system actions in the tray panel and in the quick panel."));
        return card;
    }
}

/// <summary>Vorssaint's <c>DiskExclusionsList</c>: a disclosure with the drives "Eject all disks" must never unmount.</summary>
internal sealed class DiskExclusionsList : StackPanel
{
    private const string StoreKey = "diskEjectExcludedVolumes";

    private readonly IContributionServices _ctx;
    private readonly StackPanel _body = new();
    private bool _expanded;
    private bool _adding;
    private string _draft = string.Empty;

    public DiskExclusionsList(IContributionServices ctx)
    {
        _ctx = ctx;
        _expanded = Current().Count > 0;
        Rebuild();
    }

    private List<string> Current() => QuickToggleOps.SanitizeExclusions(_ctx.App.Get<List<string>>(StoreKey));

    private void Save(List<string> list) => _ctx.App.Set(StoreKey, QuickToggleOps.SanitizeExclusions(list));

    private void Rebuild()
    {
        Children.Clear();
        _body.Children.Clear();
        var list = Current();

        var chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new RotateTransform(_expanded ? 90 : 0);
        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        header.Children.Add(UiKit.Text(Loc.T("Excluded drives"), 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        if (list.Count > 0)
        {
            var count = UiKit.Text(list.Count.ToString(), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
            Grid.SetColumn(count, 1);
            header.Children.Add(count);
        }

        Grid.SetColumn(chevron, 2);
        header.Children.Add(chevron);
        header.MouseLeftButtonUp += (_, _) =>
        {
            _expanded = !_expanded;
            Rebuild();
        };
        Children.Add(header);
        if (!_expanded)
        {
            return;
        }

        _body.Margin = new Thickness(0, 10, 0, 0);
        foreach (var name in list)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiKit.Icon("", 11, ThemeKeys.TextSecondary));
            var label = UiKit.Text(name, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var remove = new Button { Content = UiKit.Icon("", 12, ThemeKeys.TextSecondary), ToolTip = Loc.T("Remove"), Padding = new Thickness(4, 2, 4, 2), Cursor = Cursors.Hand };
            remove.SetResourceReference(StyleProperty, ThemeKeys.ButtonPlain);
            var captured = name;
            remove.Click += (_, _) =>
            {
                Save(Current().Where(n => !n.Equals(captured, StringComparison.OrdinalIgnoreCase)).ToList());
                Rebuild();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            _body.Children.Add(row);
        }

        if (_adding)
        {
            var field = new TextBox { Text = _draft, Width = 200, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Drive or volume name") };
            field.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
            var add = UiKit.Button(Loc.T("Add"), ButtonKind.Bordered, 11.5);
            var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Plain, 11.5);
            add.IsEnabled = _draft.Trim().Length > 0;
            var hint = new TextBlock { Text = Loc.T("Drive or volume name"), FontSize = 11.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Visibility = _draft.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary);

            void Commit()
            {
                var text = field.Text.Trim();
                if (text.Length == 0)
                {
                    return;
                }

                var all = Current();
                all.Add(text);
                Save(all);
                _adding = false;
                _draft = string.Empty;
                Rebuild();
            }

            field.TextChanged += (_, _) =>
            {
                _draft = field.Text;
                add.IsEnabled = field.Text.Trim().Length > 0;
                hint.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            field.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Return)
                {
                    Commit();
                }
            };
            add.Click += (_, _) => Commit();
            cancel.Click += (_, _) =>
            {
                _adding = false;
                _draft = string.Empty;
                Rebuild();
            };
            var inline = new Grid { Margin = new Thickness(0, 2, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
            inline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inline.Children.Add(field);
            inline.Children.Add(hint);
            Grid.SetColumn(add, 1);
            add.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(cancel, 2);
            cancel.Margin = new Thickness(4, 0, 0, 0);
            inline.Children.Add(add);
            inline.Children.Add(cancel);
            _body.Children.Add(inline);
            field.Loaded += (_, _) => field.Focus();
        }
        else
        {
            var candidates = ToggleDisks.OfferableIgnoringExclusions()
                .Where(v => !ToggleDisks.IsExcluded(v, list))
                .Select(v => v.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var button = UiKit.Button(Loc.T("Add drive…"), ButtonKind.Bordered, 11.5, Glyphs.Add);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 8);
            button.Click += (_, _) =>
            {
                if (candidates.Count == 0)
                {
                    _adding = true;
                    Rebuild();
                    return;
                }

                var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                foreach (var name in candidates)
                {
                    var item = new MenuItem { Header = name };
                    var captured = name;
                    item.Click += (_, _) =>
                    {
                        var all = Current();
                        all.Add(captured);
                        Save(all);
                        Rebuild();
                    };
                    menu.Items.Add(item);
                }

                menu.Items.Add(new Separator());
                var other = new MenuItem { Header = Loc.T("Other drive name…") };
                other.Click += (_, _) =>
                {
                    _adding = true;
                    Rebuild();
                };
                menu.Items.Add(other);
                menu.IsOpen = true;
            };
            _body.Children.Add(button);
        }

        _body.Children.Add(UiKit.Text(Loc.T("Drives in this list are never unmounted when using Eject all disks."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        Children.Add(_body);
    }
}


