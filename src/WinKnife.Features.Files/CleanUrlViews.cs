// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace WinKnife.Features.Files;

/// <summary>The "Clean now" box: paste a URL, see the cleaned result, copy it.</summary>
internal sealed class CleanNowBox : StackPanel
{
    private readonly IAppSettings _app;
    private readonly bool _compact;
    private readonly TextBox _input;
    private readonly Border _clear;
    private readonly StackPanel _result = new();
    private readonly Button _copy;
    private string? _cleaned;
    private string? _flash;

    public CleanNowBox(IAppSettings app, bool compact)
    {
        _app = app;
        _compact = compact;
        var size = compact ? 11.0 : 12.5;
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var host = Ui.PlaceholderBox(out _input, Strings.CleanUrl.InputPlaceholder, size);
        _input.ToolTip = Strings.CleanUrl.InputPlaceholder;
        _input.TextChanged += (_, _) =>
        {
            _flash = null;
            Evaluate();
        };
        field.Children.Add(host);
        _clear = Ui.IconButton(G.CloseCircle, Strings.CleanUrl.ClearButton, () => _input.Clear(), 22, compact ? 13 : 14, 11, filled: false);
        _clear.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_clear, 1);
        field.Children.Add(_clear);
        Children.Add(field);

        var buttons = new Grid { Margin = new Thickness(0, compact ? 8 : 10, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var paste = UiKit.Button(Strings.CleanUrl.PasteButton, ButtonKind.Bordered, compact ? 11 : 12, null, () =>
        {
            var text = ClipboardIO.ReadText();
            _input.Text = text ?? string.Empty;
        });
        buttons.Children.Add(paste);
        _copy = UiKit.Button(Strings.CleanUrl.CopyButton, ButtonKind.Prominent, compact ? 11 : 12, null, CopyResult);
        Grid.SetColumn(_copy, 2);
        buttons.Children.Add(_copy);
        Children.Add(buttons);
        _result.Margin = new Thickness(0, compact ? 8 : 10, 0, 0);
        Children.Add(_result);
        Evaluate();
    }

    private void Evaluate()
    {
        var text = _input.Text;
        _result.Children.Clear();
        _cleaned = null;
        _clear.IsEnabled = text.Length > 0;
        _clear.Opacity = text.Length > 0 ? 1 : 0.35;
        string message;
        string? shown = null;
        if (text.Trim().Length == 0)
        {
            _copy.IsEnabled = false;
            var placeholder = UiKit.Text(Strings.CleanUrl.OutputPlaceholder, _compact ? 10.5 : 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
            _result.Children.Add(placeholder);
            return;
        }

        var result = UrlCleaner.Clean(text, UrlRuleCatalog.Load(_app));
        if (result is null)
        {
            message = Strings.CleanUrl.NoUrl;
        }
        else
        {
            shown = result.Url;
            _cleaned = result.Url;
            if (_flash is not null)
            {
                message = _flash;
            }
            else if (result.Removed.Count > 0)
            {
                message = string.Format(Strings.CleanUrl.RemovedFormat, string.Join(", ", result.Removed));
            }
            else if (result.Url != text)
            {
                message = Strings.CleanUrl.Cleaned;
            }
            else
            {
                message = Strings.CleanUrl.NoChange;
            }
        }

        _copy.IsEnabled = _cleaned is not null;
        if (shown is not null)
        {
            var url = UiKit.Text(shown, _compact ? 10.5 : 11.5, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
            url.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            url.MaxHeight = (_compact ? 10.5 : 11.5) * 1.4 * 3;
            url.TextTrimming = TextTrimming.CharacterEllipsis;
            url.ToolTip = shown;
            url.Cursor = Cursors.IBeam;
            _result.Children.Add(url);
            var status = UiKit.Text(message, _compact ? 10 : 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            status.Margin = new Thickness(0, 5, 0, 0);
            _result.Children.Add(status);
        }
        else
        {
            _result.Children.Add(UiKit.Text(message, _compact ? 10 : 11.5, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true));
        }
    }

    private void CopyResult()
    {
        if (_cleaned is null)
        {
            return;
        }

        if ((Hub.CleanUrl?.CopyCleaned(_cleaned)) ?? ClipboardIO.WriteText(_cleaned, markOwn: false))
        {
            _flash = Strings.CleanUrl.Copied;
            Evaluate();
        }
    }
}

/// <summary>The "Cleaning rules" card: one disclosure per site with a checkbox grid, add-parameter row and "Add a site".</summary>
internal sealed class RulesEditor : StackPanel
{
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private readonly IAppSettings _app;
    private bool _addSiteOpen;
    private string _siteDraft = string.Empty;
    private string _siteNameDraft = string.Empty;
    private readonly Dictionary<string, string> _drafts = [];

    public RulesEditor(IAppSettings app)
    {
        _app = app;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        var rules = UrlRuleCatalog.Load(_app);
        var groups = UrlRuleCatalog.Groups(rules);
        foreach (var group in groups)
        {
            var open = Expanded.Contains(group.Key);
            Children.Add(GroupHeader(group, open));
            if (open)
            {
                Children.Add(GroupBody(group, rules));
            }
        }

        Children.Add(AddSiteSection());
        var caption = Ui.Caption(Strings.CleanUrl.RulesCaption);
        caption.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(caption);
        var coverage = Ui.Caption(Strings.CleanUrl.CoverageCaption);
        coverage.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(coverage);
    }

    private UIElement GroupHeader(RuleGroup group, bool open)
    {
        var header = new Border { Padding = new Thickness(0, 7, 0, 7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = UiKit.Icon(open ? G.ChevronDown : G.ChevronRight, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(chevron);
        var title = UiKit.Text(group.Title, 13, FontWeights.Medium, ThemeKeys.TextPrimary);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var enabled = group.EnabledCount;
        var count = UiKit.Text(enabled == 1 ? Strings.CleanUrl.CountSingular : string.Format(Strings.CleanUrl.CountPluralFormat, enabled), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        count.Margin = new Thickness(0, 0, 8, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        var allOff = enabled == 0;
        var site = Ui.IconButton(allOff ? G.Plus : G.Minus, allOff ? Strings.CleanUrl.RestoreSiteButton : Strings.CleanUrl.RemoveSiteButton, () =>
        {
            var rules = UrlRuleCatalog.Load(_app);
            UrlRuleCatalog.SetGroupEnabled(rules, group, allOff);
            UrlRuleCatalog.Save(_app, rules);
            Build();
        }, 20, 11, 10, filled: false);
        Grid.SetColumn(site, 3);
        grid.Children.Add(site);
        header.Child = grid;
        header.MouseLeftButtonUp += (_, _) =>
        {
            if (!Expanded.Add(group.Key))
            {
                Expanded.Remove(group.Key);
            }

            Build();
        };
        return header;
    }

    private UIElement GroupBody(RuleGroup group, UrlRules rules)
    {
        var body = new StackPanel { Margin = new Thickness(16, 2, 0, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = (group.Names.Count + 1) / 2;
        for (var i = 0; i < rows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < group.Names.Count; i++)
        {
            var name = group.Names[i];
            var cell = new Grid { Margin = new Thickness(0, 4, 8, 4) };
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBoxLite(name.Name, name.Enabled, on =>
            {
                var current = UrlRuleCatalog.Load(_app);
                UrlRuleCatalog.SetEnabled(current, group.Key, name.Name, on);
                UrlRuleCatalog.Save(_app, current);
                Build();
            }, 12);
            cell.Children.Add(check);
            if (name.IsUser)
            {
                var delete = Ui.IconButton(G.Minus, Strings.CleanUrl.RemoveButton, () =>
                {
                    var current = UrlRuleCatalog.Load(_app);
                    UrlRuleCatalog.RemoveName(current, group.Key, name.Name);
                    UrlRuleCatalog.Save(_app, current);
                    Build();
                }, 18, 10, 9, filled: false);
                Grid.SetColumn(delete, 1);
                cell.Children.Add(delete);
            }

            Grid.SetRow(cell, i / 2);
            Grid.SetColumn(cell, i % 2);
            grid.Children.Add(cell);
        }

        body.Children.Add(grid);

        // add-parameter row
        var addRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fieldHost = Ui.PlaceholderBox(out var field, Strings.CleanUrl.ParameterPlaceholder, 12);
        field.Text = _drafts.GetValueOrDefault(group.Key, string.Empty);
        var add = UiKit.Button(Strings.CleanUrl.AddButton, ButtonKind.Bordered, 12);
        add.Margin = new Thickness(8, 0, 0, 0);
        add.IsEnabled = UrlRuleCatalog.NormalizeName(field.Text) is not null;
        field.TextChanged += (_, _) =>
        {
            _drafts[group.Key] = field.Text;
            add.IsEnabled = UrlRuleCatalog.NormalizeName(field.Text) is not null;
        };
        void Commit()
        {
            var name = UrlRuleCatalog.NormalizeName(field.Text);
            if (name is null)
            {
                return;
            }

            var current = UrlRuleCatalog.Load(_app);
            UrlRuleCatalog.AddName(current, group.Key, name);
            UrlRuleCatalog.Save(_app, current);
            _drafts.Remove(group.Key);
            Build();
        }

        add.Click += (_, _) => Commit();
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        addRow.Children.Add(fieldHost);
        Grid.SetColumn(add, 1);
        addRow.Children.Add(add);
        body.Children.Add(addRow);
        var match = Ui.Caption(Strings.CleanUrl.MatchCaption, 11);
        match.Margin = new Thickness(0, 6, 0, 0);
        match.Opacity = 0.8;
        body.Children.Add(match);
        return body;
    }

    private UIElement AddSiteSection()
    {
        var host = new StackPanel();
        var header = new Border { Padding = new Thickness(0, 7, 0, 7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var chevron = UiKit.Icon(_addSiteOpen ? G.ChevronDown : G.ChevronRight, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.Width = 16;
        chevron.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(chevron);
        row.Children.Add(UiKit.Text(Strings.CleanUrl.AddSite, 13, FontWeights.Medium, ThemeKeys.TextPrimary));
        header.Child = row;
        header.MouseLeftButtonUp += (_, _) =>
        {
            _addSiteOpen = !_addSiteOpen;
            Build();
        };
        host.Children.Add(header);
        if (_addSiteOpen)
        {
            var grid = new Grid { Margin = new Thickness(16, 2, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var siteHost = Ui.PlaceholderBox(out var site, Strings.CleanUrl.SitePlaceholder, 12);
            site.Text = _siteDraft;
            var nameHost = Ui.PlaceholderBox(out var name, Strings.CleanUrl.ParameterPlaceholder, 12);
            name.Text = _siteNameDraft;
            nameHost.Margin = new Thickness(8, 0, 0, 0);
            var add = UiKit.Button(Strings.CleanUrl.AddButton, ButtonKind.Bordered, 12);
            add.Margin = new Thickness(8, 0, 0, 0);
            void Validate()
            {
                _siteDraft = site.Text;
                _siteNameDraft = name.Text;
                add.IsEnabled = UrlRuleCatalog.NormalizeSiteKey(site.Text) is not null && UrlRuleCatalog.NormalizeName(name.Text) is not null;
            }

            site.TextChanged += (_, _) => Validate();
            name.TextChanged += (_, _) => Validate();
            Validate();
            void Commit()
            {
                var key = UrlRuleCatalog.NormalizeSiteKey(site.Text);
                var parameter = UrlRuleCatalog.NormalizeName(name.Text);
                if (key is null || parameter is null)
                {
                    return;
                }

                var current = UrlRuleCatalog.Load(_app);
                UrlRuleCatalog.AddName(current, key, parameter);
                UrlRuleCatalog.Save(_app, current);
                Expanded.Add(key);
                _siteDraft = string.Empty;
                _siteNameDraft = string.Empty;
                Build();
            }

            add.Click += (_, _) => Commit();
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
            grid.Children.Add(siteHost);
            Grid.SetColumn(nameHost, 1);
            grid.Children.Add(nameHost);
            Grid.SetColumn(add, 2);
            grid.Children.Add(add);
            host.Children.Add(grid);
        }

        return host;
    }
}

/// <summary>Clean URL page: switch + status, cleaning rules, "Clean now".</summary>
public sealed class CleanUrlSettingsSection : ISettingsSection
{
    public string? FeatureId => "url-cleaner";

    public string PageId => SettingsPages.CleanUrl;

    public string? Anchor => "urlCleaner";

    public string? Title => Loc.T("Clean URL");

    public IReadOnlyList<string> Keywords => ["tracking", "utm", "parameters", "links", "Clean now", "Cleaning rules", "Add a site"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        CleanUrlFeature.RegisterDefaults(c.App);
        var host = Hub.CleanUrl;
        var rows = new List<UIElement?>
        {
            SettingsKit.Toggle(c.App, "urlCleanerEnabled", Strings.CleanUrl.Enable, Strings.CleanUrl.EnableCaption, _ => c.RequestRebuild()),
            Ui.Caption(Strings.CleanUrl.LocalNote, 11),
        };
        if (c.App.Get<bool>("urlCleanerEnabled") && host is { IsRunning: true })
        {
            rows.Add(Ui.ActiveLabel(Strings.CleanUrl.ActiveNow));
            if (host.LastRemoved.Count > 0)
            {
                rows.Add(Ui.Caption(string.Format(Strings.CleanUrl.RemovedFormat, string.Join(", ", host.LastRemoved))));
            }
        }

        var card2 = new SettingsCard();
        card2.Add(new RulesEditor(c.App));
        var card2Host = new StackPanel();
        var title = UiKit.Text(Strings.CleanUrl.RulesTitle, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        title.Margin = new Thickness(6, 0, 0, 6);
        card2Host.Children.Add(title);
        card2Host.Children.Add(card2);

        var card3 = new SettingsCard();
        card3.Add(new CleanNowBox(c.App, compact: false));
        var card3Host = new StackPanel();
        var title3 = UiKit.Text(Strings.CleanUrl.ManualTitle, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        title3.Margin = new Thickness(6, 0, 0, 6);
        card3Host.Children.Add(title3);
        card3Host.Children.Add(card3);

        return SettingsKit.Page(SettingsKit.Section(null, [.. rows]), card2Host, card3Host);
    }
}

/// <summary>The Utilities-tab row "Clean URL" that hosts the compact cleaner panel.</summary>
public sealed class CleanUrlUtility : IPanelUtility
{
    public string? FeatureId => "url-cleaner";

    public string Id => "cleanURL";

    public string Title => Loc.T("Clean URL");

    public string Glyph => "";

    public int Order => 100;

    public string? Caption => Strings.CleanUrl.EnableCaption;

    public string? SettingsDestination => SettingsPages.CleanUrl;

    public FrameworkElement? CreateHostedView(IPanelContext context) => new CleanUrlPanelView(context);
}

/// <summary>Hosted panel view of the URL cleaner (spec 01 6.2).</summary>
internal sealed class CleanUrlPanelView : StackPanel
{
    public CleanUrlPanelView(IPanelContext context)
    {
        context.SetKeepOpen(true);
        CleanUrlFeature.RegisterDefaults(context.App);
        var app = context.App;
        var host = Hub.CleanUrl;

        var first = new StackPanel();
        var status = new StackPanel();
        void Refresh()
        {
            status.Children.Clear();
            if (app.Get<bool>("urlCleanerEnabled") && host is { IsRunning: true })
            {
                status.Children.Add(Ui.ActiveLabel(Strings.CleanUrl.ActiveNow, 10));
                status.Margin = new Thickness(0, 7, 0, 0);
            }
        }

        first.Children.Add(new CheckBoxLite(Strings.CleanUrl.Enable, app.Get<bool>("urlCleanerEnabled"), on =>
        {
            app.Set("urlCleanerEnabled", on);
            Refresh();
        }, 11.5));
        var caption = UiKit.Text(Strings.CleanUrl.EnableCaption, 10, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 7, 0, 0);
        first.Children.Add(caption);
        first.Children.Add(status);
        Refresh();
        Children.Add(new PanelCard(first));

        var second = new StackPanel();
        second.Children.Add(UiKit.Text(Strings.CleanUrl.ManualTitle, 10, FontWeights.SemiBold, ThemeKeys.TextTertiary));
        var box = new CleanNowBox(app, compact: true) { Margin = new Thickness(0, 7, 0, 0) };
        second.Children.Add(box);
        var card = new PanelCard(second) { Margin = new Thickness(0, 10, 0, 0) };
        Children.Add(card);
    }
}
