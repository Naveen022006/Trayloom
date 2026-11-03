// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Glyphs = Trayloom.Core.Ui.Glyphs;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Agents;

/// <summary>
/// Settings &gt; Dynamic Island &gt; Content &gt; "AI Agents": the providers, the Claude account and Codex accounts groups, the cards, the
/// limit display, the live strip, the alerts and the Claude plan limits status (spec 02d section 10).
/// </summary>
public sealed class AgentsSettingsSection : ISettingsSection
{
    /// <summary>Anchor of the section on the Dynamic Island page (<c>island#agents</c>).</summary>
    public const string AnchorId = "agents";

    public string? FeatureId => AgentKeys.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => AnchorId;

    public string? Title => AgentStrings.Title;

    public IReadOnlyList<string> Keywords =>
    [
        "Claude", "Codex", "OpenCode", "GitHub Copilot", "tokens", "API value", "plan limits", "usage", "agents", "Claude account", "Codex accounts",
        "Cards", "Alerts", "Daily API value budget", "Keep prices up to date", "Show limits as", "Limit to show", "Warn at", "When a task finishes",
    ];

    public int Order => 160;

    public FrameworkElement CreateView(ISettingsContext context) => new AgentsSettingsView(context).Build();
}

/// <summary>Builds the settings card of the AI Agents extension.</summary>
internal sealed class AgentsSettingsView
{
    private readonly ISettingsContext _context;
    private readonly IAppSettings _app;
    private readonly AgentSettings _settings;
    private readonly AgentsRuntime? _runtime;

    public AgentsSettingsView(ISettingsContext context)
    {
        _context = context;
        _app = context.App;
        AgentSettings.RegisterDefaults(_app);
        _settings = new AgentSettings(_app, context.Runtime);
        _runtime = context.Services.Get<AgentsRuntime>();
    }

    private static TextBlock Subheading(string text)
    {
        var t = UiKit.Text(text, 13, FontWeights.Medium, ThemeKeys.TextPrimary);
        return t;
    }

    private static FrameworkElement Indent(UIElement child) => new Border { Child = child, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };

    private SettingsRow Switch(string glyph, string title, string key, bool fallback, string? caption = null, bool rebuild = false) =>
        new(glyph, title, caption, new MacSwitch(_app.Get(key, fallback), SwitchSize.Regular, on =>
        {
            _app.Set(key, on);
            if (rebuild)
            {
                _context.RequestRebuild();
            }
        }));

    private SettingsRow Menu<T>(string glyph, string title, IReadOnlyList<(T Value, string Label)> items, T selected, Action<T> onChanged)
        where T : notnull =>
        new(glyph, title, null, MenuCombo(items, selected, onChanged));

    private static ComboBox MenuCombo<T>(IReadOnlyList<(T Value, string Label)> items, T selected, Action<T> onChanged, double width = 150)
    {
        var combo = new ComboBox { MinWidth = width, ItemsSource = items.Select(i => i.Label).ToList() };
        var index = items.ToList().FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected));
        combo.SelectedIndex = index < 0 ? 0 : index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(items[combo.SelectedIndex].Value);
            }
        };
        return combo;
    }

    public FrameworkElement Build()
    {
        var card = new SettingsCard();
        card.Add(UiKit.Text(AgentStrings.SettingsDescription, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));

        // Which logs exist is checked when the page opens, not on every draw.
        var found = AgentLogRoot.All(ClaudeAccountProfile.Selected(_app)).Where(r => r.Exists).Select(r => r.Provider).ToHashSet();
        var enabled = AgentProviders.All.Where(p => _app.Get(p.SettingKey(), true)).ToList();
        foreach (var provider in AgentProviders.All)
        {
            card.Add(ProviderRow(provider, found.Contains(provider), enabled));
        }

        var claude = _app.Get(AgentKeys.Claude, true);
        var codex = _app.Get(AgentKeys.Codex, true);
        if (claude)
        {
            card.Add(ClaudeAccountGroup());
        }

        if (codex)
        {
            card.Add(CodexAccountsGroup());
        }

        card.AddDivider();
        card.Add(Subheading(AgentStrings.CardsTitle));
        card.Add(CardsEditor());
        card.AddCaption(AgentStrings.CardsHint);
        if (codex && !_settings.HiddenCards().Contains(AgentCard.Resets))
        {
            card.AddCaption(AgentStrings.ResetsHelp);
        }

        var used = new SegmentedControl([AgentStrings.Remaining, AgentStrings.Used], _app.Get(AgentKeys.LimitDisplay, "remaining") == "used" ? 1 : 0, false, 12.5);
        used.SelectionChanged += i => _app.Set(AgentKeys.LimitDisplay, i == 1 ? "used" : "remaining");
        card.Add(new SettingsRow(AgentCard.Limits.Glyph(), AgentStrings.LimitsAs, null, used));
        // Claude Ultra has no 5-hour limit, so the menu does not offer one (unless another agent in use has it).
        var focusOptions = new List<(string, string)> { ("mostUsed", AgentStrings.MostUsed) };
        var snapshot = _runtime?.Service.Snapshot ?? new AgentUsageSnapshot();
        var hasSession = AgentLimitPlan.AnySessionWindow(snapshot, enabled);
        if (hasSession)
        {
            focusOptions.Add(("session", AgentStrings.FocusSession));
        }

        focusOptions.Add(("weekly", AgentStrings.FocusWeekly));
        var focusNow = _app.Get(AgentKeys.LimitFocus, "mostUsed");
        card.Add(Menu("", AgentStrings.LimitFocus,
            focusOptions, !hasSession && focusNow == "session" ? "mostUsed" : focusNow, v => _app.Set(AgentKeys.LimitFocus, v)));

        card.AddDivider();
        card.Add(Subheading(AgentStrings.LiveTitle));
        card.Add(Switch(AgentCard.Live.Glyph(), AgentStrings.LiveActivity, AgentKeys.LiveActivity, true, rebuild: true));
        if (_app.Get(AgentKeys.LiveActivity, true))
        {
            card.Add(Indent(Menu("", AgentStrings.Readout,
                [("timeLimit", AgentStrings.ReadoutTimeLimit), ("elapsed", AgentStrings.ReadoutElapsed), ("limit", AgentStrings.ReadoutLimit), ("tokens", AgentStrings.ReadoutTokens), ("cost", AgentStrings.ReadoutCost)],
                AgentSettings.ReadoutKey(_app.Get(AgentKeys.Readout, AgentSettings.DefaultReadout)), v =>
                {
                    _app.Set(AgentKeys.Readout, v);
                    _context.RequestRebuild();
                })));
            card.Add(Indent(StripSample(enabled)));
        }

        card.AddDivider();
        card.Add(Subheading(AgentStrings.Alerts));
        card.Add(Switch("", AgentStrings.FinishAlert, AgentKeys.FinishAlert, true, rebuild: true));
        if (_app.Get(AgentKeys.FinishAlert, true))
        {
            card.Add(Indent(Menu("", AgentStrings.FinishAfter,
                AgentSettings.FinishMinimums.Select(s => (s, s == 0 ? AgentStrings.AnyLength : AgentFormat.DurationShort(s))).ToList(),
                Nearest(_app.Get(AgentKeys.FinishMinimum, AgentSettings.DefaultFinishMinimum), AgentSettings.FinishMinimums), v => _app.Set(AgentKeys.FinishMinimum, v))));
        }

        card.Add(Switch("", AgentStrings.LimitAlert, AgentKeys.LimitAlert, true, rebuild: true));
        if (_app.Get(AgentKeys.LimitAlert, true))
        {
            card.Add(Indent(Menu("", AgentStrings.LimitAt,
                AgentSettings.LimitThresholds.Select(v => (v, AgentStrings.UsedShare(AgentFormat.Percent(v / 100)))).ToList(),
                Nearest(_app.Get(AgentKeys.LimitThreshold, AgentSettings.DefaultLimitThreshold), AgentSettings.LimitThresholds), v => _app.Set(AgentKeys.LimitThreshold, v))));
        }

        card.AddDivider();
        card.Add(new AgentAttentionSettings(_app));
        card.AddDivider();
        card.Add(Menu("", AgentStrings.Budget,
            AgentSettings.Budgets.Select(v => (v, v == 0 ? AgentStrings.Off : AgentFormat.Cost(v))).ToList(),
            Nearest(_app.Get(AgentKeys.DailyBudget, 0.0), AgentSettings.Budgets), v => _app.Set(AgentKeys.DailyBudget, v)));
        card.AddCaption(AgentStrings.ValueNote);
        var priceCaption = AgentStrings.PriceUpdatesHint;
        if (AgentPricing.List.Updated != AgentPriceList.Empty.Updated)
        {
            priceCaption += " " + AgentStrings.PricesFrom(AgentFormat.Day(AgentPricing.List.Updated));
        }

        card.Add(Switch("", AgentStrings.PriceUpdates, AgentKeys.PriceUpdates, true, priceCaption));

        if (claude)
        {
            card.AddDivider();
            card.Add(Subheading(AgentStrings.ClaudeLimitsTitle));
            card.Add(new ClaudeLimitsStatus(_runtime, _app, _settings));
            if (ClaudeAccountProfile.Selected(_app).IsDefault)
            {
                card.AddCaption(AgentStrings.ClaudeLimitsPrivacy);
            }
        }

        if (claude || codex)
        {
            card.AddDivider();
            card.Add(new LimitDiagnosticsView(_runtime, Environment.GetEnvironmentVariable("TRAYLOOM_OPEN_DIAGNOSTICS") == "1"));
        }

        return card;
    }

    private static double Nearest(double value, double[] options) => options.OrderBy(o => Math.Abs(o - value)).First();

    // ------------------------------------------------------------------------------------------ providers

    private FrameworkElement ProviderRow(AgentProvider provider, bool found, List<AgentProvider> enabled)
    {
        var tile = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = AgentColors.Of(provider.Tint(), 0.14),
            Child = AgentMarks.Create(provider, 13 * AgentMarks.Scale(provider) * 0.92),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var on = enabled.Contains(provider);
        // One agent stays on; turning the whole section off stops all.
        var last = on && enabled.Count <= 1;
        var toggle = new MacSwitch(on, SwitchSize.Regular, value =>
        {
            _app.Set(provider.SettingKey(), value);
            _context.RequestRebuild();
        })
        { IsEnabled = !last, VerticalAlignment = VerticalAlignment.Center };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(provider.DisplayName(), 13, FontWeights.Normal, ThemeKeys.TextPrimary));
        var caption = UiKit.Text(found ? AgentStrings.Found : AgentStrings.NotFound, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        caption.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(caption);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);
        return grid;
    }

    // ------------------------------------------------------------------------------------------ Claude account

    private sealed record ProfileEntry(ClaudeAccountProfile Profile, ClaudeAccountProfile.Identity? Identity, bool Available)
    {
        public string Label => Profile.Name(AgentStrings.DefaultAccount) + (Identity?.Email is { } e ? " · " + e : "");
    }

    private FrameworkElement ClaudeAccountGroup()
    {
        var home = AgentLogRoot.Home;
        var selected = ClaudeAccountProfile.Selected(_app);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ProfileEntry>();
        foreach (var profile in new[] { new ClaudeAccountProfile(), selected }.Concat(ClaudeAccountProfile.Discover(home)))
        {
            if (seen.Add(profile.Id))
            {
                entries.Add(new ProfileEntry(profile, profile.Read(home).Identity, profile.IsAvailable(home)));
            }
        }

        var group = new StackPanel();
        var combo = MenuCombo(entries.Select(e => (e.Profile.Id, e.Label)).ToList(), selected.Id, id => Select(id), 220);
        group.Children.Add(new SettingsRow("", AgentStrings.ClaudeAccountTitle, null, combo));
        var entry = entries.FirstOrDefault(e => e.Profile.Id == selected.Id);
        if (entry is not null)
        {
            if (!entry.Available)
            {
                group.AddSpaced(Caption(AgentStrings.ClaudeInvalidFolder, ThemeKeys.Orange), 8);
            }
            else if (entry.Identity is null)
            {
                group.AddSpaced(Caption(AgentStrings.UnknownAccount, ThemeKeys.TextSecondary), 8);
            }

            if (!selected.IsDefault)
            {
                var path = UiKit.Text(selected.Directory, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                path.ToolTip = selected.Directory;
                group.AddSpaced(path, 4);
            }
        }

        var choose = UiKit.Button(AgentStrings.ChooseAccountFolder, ButtonKind.Bordered, 12, null, ChooseClaudeFolder);
        choose.HorizontalAlignment = HorizontalAlignment.Left;
        group.AddSpaced(choose, 8);
        group.AddSpaced(Caption(AgentStrings.ClaudeAccountExplanation, ThemeKeys.TextSecondary), 8);
        var link = UiKit.Text(AgentStrings.SignInHelp, 11.5, FontWeights.Normal, ThemeKeys.Accent);
        link.Cursor = Cursors.Hand;
        link.TextDecorations = TextDecorations.Underline;
        link.MouseLeftButtonUp += (_, _) => UiKit.OpenUrl(AgentStrings.SignInHelpUrl);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        group.AddSpaced(link, 8);
        return group;
    }

    private static TextBlock Caption(string text, string brushKey) => UiKit.Text(text, 11.5, FontWeights.Normal, brushKey, wrap: true);

    private void Select(string directory)
    {
        _app.Set(AgentKeys.ClaudeProfile, directory);
        _context.RequestRebuild();
    }

    private void ChooseClaudeFolder()
    {
        var home = AgentLogRoot.Home;
        var dialog = new OpenFolderDialog { InitialDirectory = home, Title = AgentStrings.ChooseAccountFolder };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folder = dialog.FolderName;
        var profile = new ClaudeAccountProfile(folder);
        // The ordinary ~\.claude folder uses ~\.claude.json. An explicit profile in that folder instead has its own nested .claude.json.
        var usual = Path.Combine(home, ".claude");
        if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), usual, StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(folder, ".claude.json")))
        {
            Select("");
        }
        else if (profile.IsAvailable(home))
        {
            Select(profile.Directory);
        }
        else
        {
            MessageBox.Show(AgentStrings.ClaudeInvalidFolder, AgentStrings.ClaudeAccountTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ------------------------------------------------------------------------------------------ Codex accounts

    private FrameworkElement CodexAccountsGroup()
    {
        var profiles = CodexAccountProfile.Decode(_app.Get(AgentKeys.CodexProfiles, ""));
        var group = new StackPanel();
        group.Children.Add(Subheading(AgentStrings.CodexAccountsTitle));
        group.AddSpaced(Caption(AgentStrings.CodexAccountsCaption, ThemeKeys.TextSecondary), 6);
        foreach (var profile in profiles)
        {
            group.AddSpaced(CodexRow(profile, profiles), 8);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var full = profiles.Count >= CodexAccountProfile.Maximum;
        var find = UiKit.Button(AgentStrings.FindAccounts, ButtonKind.Bordered, 12, null, () =>
        {
            var list = profiles.ToList();
            foreach (var found in CodexAccountProfile.Discover(AgentLogRoot.Home))
            {
                if (list.Count < CodexAccountProfile.Maximum && !list.Any(p => string.Equals(p.Directory, found.Directory, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(found);
                }
            }

            SaveCodex(list);
        });
        find.IsEnabled = !full;
        var add = UiKit.Button(AgentStrings.AddFolder, ButtonKind.Bordered, 12, null, () => AddCodexFolder(profiles));
        add.IsEnabled = !full;
        add.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(find);
        buttons.Children.Add(add);
        if (profiles.Count > 0)
        {
            var states = _runtime?.Accounts.States ?? [];
            var check = UiKit.Button(AgentStrings.CheckLimits, ButtonKind.Bordered, 12, null, () => _runtime?.Accounts.Refresh(null, true));
            check.IsEnabled = _runtime is not null && _runtime.Accounts.IsEnabled && !(states.Count > 0 && states.All(s => s.Checking));
            check.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(check);
        }

        group.AddSpaced(buttons, 8);
        group.AddSpaced(Caption(profiles.Count == 0 ? AgentStrings.CodexFallback : AgentStrings.CodexResetsNote, ThemeKeys.TextSecondary), 8);
        return group;
    }

    private FrameworkElement CodexRow(CodexAccountProfile profile, List<CodexAccountProfile> all)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var name = new TextBox { Text = profile.Name, MaxLength = 40, FontSize = 13, Padding = new Thickness(6, 3, 6, 3) };
        name.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        name.ToolTip = AgentStrings.AccountName + ": " + profile.Name;
        // Writes the name on each valid edit; empty or whitespace-only edits are ignored.
        name.TextChanged += (_, _) =>
        {
            var trimmed = name.Text.Trim();
            if (trimmed.Length == 0 || trimmed == profile.Name)
            {
                return;
            }

            var list = all.Select(p => p.Id == profile.Id ? new CodexAccountProfile { Id = p.Id, Name = trimmed.Length > 40 ? trimmed[..40] : trimmed, Directory = p.Directory } : p).ToList();
            _app.Set(AgentKeys.CodexProfiles, CodexAccountProfile.Encode(list));
        };
        text.Children.Add(name);
        var path = UiKit.Text(profile.Directory, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        path.ToolTip = profile.Directory;
        path.Margin = new Thickness(2, 3, 0, 0);
        text.Children.Add(path);
        grid.Children.Add(text);
        var remove = new Button { Content = UiKit.Icon("", 15, ThemeKeys.TextSecondary), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6), Cursor = Cursors.Hand, ToolTip = AgentStrings.RemoveAccount, Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => SaveCodex(all.Where(p => p.Id != profile.Id).ToList());
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        return grid;
    }

    private void AddCodexFolder(List<CodexAccountProfile> profiles)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = AgentLogRoot.Home, Title = AgentStrings.AddFolder };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var candidate = new CodexAccountProfile { Id = Guid.NewGuid().ToString(), Name = CodexAccountProfile.NameFor(Path.GetFileName(dialog.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : "Codex"), Directory = dialog.FolderName };
        if (candidate.CanonicalDirectory() is not { } canonical)
        {
            MessageBox.Show(AgentStrings.CodexInvalidFolder, AgentStrings.ChooseCodexFolder, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (profiles.Any(p => string.Equals(p.Directory, canonical, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidate.Directory = canonical;
        SaveCodex(profiles.Append(candidate).ToList());
    }

    private void SaveCodex(List<CodexAccountProfile> list)
    {
        _app.Set(AgentKeys.CodexProfiles, CodexAccountProfile.Encode(list));
        _runtime?.Accounts.Synchronize();
        _context.RequestRebuild();
    }

    // ------------------------------------------------------------------------------------------ cards

    private FrameworkElement CardsEditor()
    {
        var wrap = new AgentWrapGrid { MinCell = 104, Spacing = 8 };
        var order = _settings.OrderedCards();
        var hidden = _settings.HiddenCards();

        void Reorder(AgentCard card, AgentCard before)
        {
            var list = _settings.OrderedCards();
            list.Remove(card);
            list.Insert(list.IndexOf(before), card);
            _settings.SetCardOrder(list);
            _context.RequestRebuild();
        }

        void Move(AgentCard card, int offset)
        {
            var list = _settings.OrderedCards();
            var index = list.IndexOf(card);
            if (index + offset < 0 || index + offset >= list.Count)
            {
                return;
            }

            (list[index], list[index + offset]) = (list[index + offset], list[index]);
            _settings.SetCardOrder(list);
            _context.RequestRebuild();
        }

        foreach (var card in order)
        {
            var included = !hidden.Contains(card);
            var item = new CardEditorItem(card, included);
            item.Toggled += () =>
            {
                _settings.SetCardHidden(card, included);
                _context.RequestRebuild();
            };
            item.DroppedOn += dragged => Reorder(dragged, card);
            item.MoveRequested += offset => Move(card, offset);
            wrap.Children.Add(item);
        }

        return wrap;
    }

    // ------------------------------------------------------------------------------------------ strip sample

    /// <summary>The closed island while an agent works, drawn small beside its option: the mark on one side and the chosen reading on the other.</summary>
    private FrameworkElement StripSample(List<AgentProvider> enabled)
    {
        var provider = enabled.FirstOrDefault();
        var readout = _settings.Readout();
        var focus = _settings.Focus();
        var used = _settings.UsedDisplay();
        var service = _runtime?.Service;
        var pill = new Border
        {
            Height = 24,
            Width = 190,
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 0, 10, 0),
        };
        var grid = new Grid();
        pill.Child = grid;
        var reading = new TextBlock { FontSize = 12, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = UiKit.TextFont };
        Typography.SetNumeralAlignment(reading, FontNumeralAlignment.Tabular);

        void Fill()
        {
            // A turn in progress shows its own numbers; otherwise an example does.
            var real = service?.Snapshot ?? new AgentUsageSnapshot();
            var now = DateTime.UtcNow;
            var sample = AgentSettings.StripSampleSnapshot(real, provider, focus, now);
            var working = AgentProviders.All.FirstOrDefault(p => sample.Live.Any(l => l.Provider == p));
            grid.Children.Clear();
            var glyph = new AgentGlyph(working, 11) { HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(glyph);
            var info = AgentSettings.Strip(sample, readout, used, focus, now);
            reading.Inlines.Clear();
            reading.Foreground = AgentColors.Of(working.Tint());
            if (info.Lead.Length > 0)
            {
                reading.Inlines.Add(new System.Windows.Documents.Run(info.Lead));
            }

            if (info.Limit is not null)
            {
                if (info.Lead.Length > 0)
                {
                    reading.Inlines.Add(new System.Windows.Documents.Run(" · ") { Foreground = AgentColors.White(0.45) });
                }

                reading.Inlines.Add(new System.Windows.Documents.Run(info.Limit) { Foreground = AgentColors.Of(AgentColors.LimitTint(working, info.Window?.UsedFraction ?? 0)) });
            }

            grid.Children.Add(reading);
        }

        Fill();
        if (readout is AgentReadout.Elapsed or AgentReadout.Limit or AgentReadout.TimeAndLimit)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => Fill();
            pill.Loaded += (_, _) => timer.Start();
            pill.Unloaded += (_, _) => timer.Stop();
        }

        return pill;
    }
}

/// <summary>An adaptive grid: as many columns of at least <see cref="MinCell"/> as fit, cells share the width.</summary>
internal sealed class AgentWrapGrid : Panel
{
    public double MinCell { get; set; } = 104;

    public double Spacing { get; set; } = 8;

    private (int Columns, double Cell) Layout(double width)
    {
        var columns = Math.Max(1, (int)Math.Floor((width + Spacing) / (MinCell + Spacing)));
        return (columns, (width - Spacing * (columns - 1)) / columns);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        var (columns, cell) = Layout(width);
        double rowHeight = 0, total = 0;
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++i % columns == 0)
            {
                total += rowHeight + Spacing;
                rowHeight = 0;
            }
        }

        if (i % columns != 0)
        {
            total += rowHeight + Spacing;
        }

        return new Size(width, Math.Max(0, total - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, cell) = Layout(finalSize.Width);
        double y = 0, rowHeight = 0;
        var i = 0;
        var rowStart = 0;
        var heights = new List<double>();
        foreach (UIElement child in InternalChildren)
        {
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++i % columns == 0 || i == InternalChildren.Count)
            {
                heights.Add(rowHeight);
                rowHeight = 0;
            }
        }

        i = 0;
        foreach (UIElement child in InternalChildren)
        {
            var row = i / columns;
            var column = i % columns;
            var top = heights.Take(row).Sum() + row * Spacing;
            child.Arrange(new Rect(column * (cell + Spacing), top, cell, heights[row]));
            i++;
        }

        _ = y;
        _ = rowStart;
        return finalSize;
    }
}

/// <summary>A toggle card of the cards editor (spec 02d 3.3): glyph, title and a corner check; draggable to reorder.</summary>
internal sealed class CardEditorItem : Border
{
    private Point _start;

    public CardEditorItem(AgentCard card, bool included)
    {
        MinHeight = 76;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(10);
        Cursor = Cursors.Hand;
        AllowDrop = true;
        this.Themed(BackgroundProperty, ThemeKeys.TileFill);
        if (included)
        {
            this.Themed(BorderBrushProperty, ThemeKeys.AccentFill18);
            BorderThickness = new Thickness(0);
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(UiKit.Icon(card.Glyph(), 20, included ? ThemeKeys.Accent : ThemeKeys.TextSecondary));
        var title = UiKit.Text(AgentStrings.Card(card), 11, FontWeights.Medium, ThemeKeys.TextPrimary, wrap: true);
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        title.Height = 28;
        title.MaxHeight = 28;
        stack.Children.Add(title);
        var grid = new Grid { Children = { stack } };
        var corner = UiKit.Icon(included ? Glyphs.CheckCircle : Glyphs.Add, 15, included ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        corner.HorizontalAlignment = HorizontalAlignment.Right;
        corner.VerticalAlignment = VerticalAlignment.Top;
        corner.Margin = new Thickness(0, -6, -6, 0);
        grid.Children.Add(corner);
        Child = grid;
        ToolTip = AgentStrings.Card(card);
        var menu = new ContextMenu();
        var up = new MenuItem { Header = AgentStrings.MoveUp };
        up.Click += (_, _) => MoveRequested?.Invoke(-1);
        var down = new MenuItem { Header = AgentStrings.MoveDown };
        down.Click += (_, _) => MoveRequested?.Invoke(1);
        menu.Items.Add(up);
        menu.Items.Add(down);
        ContextMenu = menu;

        MouseLeftButtonDown += (_, e) => _start = e.GetPosition(this);
        MouseLeftButtonUp += (_, _) => Toggled?.Invoke();
        MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _start.X) < 6 && Math.Abs(p.Y - _start.Y) < 6)
            {
                return;
            }

            Opacity = 0.45;
            DragDrop.DoDragDrop(this, new DataObject("trayloom.agentCard", card.Raw()), DragDropEffects.Move);
            Opacity = 1;
        };
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("trayloom.agentCard") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData("trayloom.agentCard") is string raw && AgentCards.Parse(raw) is { } dragged && dragged != card)
            {
                DroppedOn?.Invoke(dragged);
            }

            e.Handled = true;
        };
    }

    public event Action? Toggled;

    public event Action<AgentCard>? DroppedOn;

    public event Action<int>? MoveRequested;
}

/// <summary>The "Claude plan limits" status line, refreshed every 30 seconds (spec 02d 10.1 item 8).</summary>
internal sealed class ClaudeLimitsStatus : Border
{
    private readonly AgentsRuntime? _runtime;
    private readonly IAppSettings _app;
    private readonly AgentSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly bool _appInstalled;
    private DateTime? _fileCheck;

    public ClaudeLimitsStatus(AgentsRuntime? runtime, IAppSettings app, AgentSettings settings)
    {
        _runtime = runtime;
        _app = app;
        _settings = settings;
        _appInstalled = ClaudeAccountProfile.Selected(app).IsDefault && ClaudeAppUsage.AppInstalled();
        // The file is looked at when the page opens, off the UI thread.
        Task.Run(ClaudeAppUsage.LastCheck).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _fileCheck = t.Result;
                    Build();
                });
            }
        });
        Build();
        Loaded += (_, _) =>
        {
            _timer.Tick += OnTick;
            _timer.Start();
            if (_runtime is not null)
            {
                _runtime.Service.Changed += OnChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            if (_runtime is not null)
            {
                _runtime.Service.Changed -= OnChanged;
            }
        };
    }

    private void OnTick(object? sender, EventArgs e) => Build();

    private void OnChanged() => Dispatcher.BeginInvoke(Build);

    private static string Relative(DateTime date, DateTime now) => AgentFormat.Relative(date > now ? now : date, now, false);

    private void Build()
    {
        var now = DateTime.UtcNow;
        var status = _runtime?.Service.Status ?? new AgentServiceStatus();
        var snapshot = _runtime?.Service.Snapshot ?? new AgentUsageSnapshot();
        var account = snapshot.Limits.TryGetValue(AgentProvider.Claude, out var l) && l.Source == LimitSource.Account ? l.ObservedAt : (DateTime?)null;
        var checkedAt = account ?? status.ClaudeAppChecked ?? _fileCheck;
        var accountFresh = account is { } a && now - a < ClaudeAppUsage.Freshness;
        var failure = account is not null && accountFresh ? null : status.ClaudeAccountFailure is { } f ? AgentStrings.ClaudeCode(f) : null;
        var defaultProfile = ClaudeAccountProfile.Selected(_app).IsDefault;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void Icon(string glyph, string brush)
        {
            var icon = UiKit.Icon(glyph, 14, brush);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 1, 10, 0);
            row.Children.Add(icon);
        }

        void Texts(params string?[] lines)
        {
            var stack = new StackPanel();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i] is not { } text)
                {
                    continue;
                }

                var t = UiKit.Text(text, 12.5, FontWeights.Normal, i == 0 || lines.Length == 1 ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, wrap: true);
                if (stack.Children.Count > 0)
                {
                    t.Margin = new Thickness(0, 4, 0, 0);
                }

                stack.Children.Add(t);
            }

            Grid.SetColumn(stack, 1);
            row.Children.Add(stack);
        }

        void ButtonAt(string label, Action click)
        {
            var b = UiKit.Button(label, ButtonKind.Bordered, 12, null, click);
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(b, 2);
            row.Children.Add(b);
        }

        if (account is { } at)
        {
            Icon(accountFresh ? Glyphs.CheckCircle : "", accountFresh ? ThemeKeys.Green : ThemeKeys.Orange);
            Texts("Claude Code · " + AgentStrings.Updated(Relative(at, now)), failure);
        }
        else if (!defaultProfile)
        {
            Icon(Glyphs.Info, ThemeKeys.TextSecondary);
            Texts(failure ?? AgentStrings.NoLimitsYet);
        }
        else if (checkedAt is { } c && now - c < ClaudeAppUsage.Freshness)
        {
            Icon(Glyphs.CheckCircle, ThemeKeys.Green);
            Texts(AgentStrings.ClaudeLimitsCurrent(Relative(c, now)), failure);
        }
        else if (!_appInstalled && checkedAt is null)
        {
            Icon(Glyphs.Info, ThemeKeys.TextSecondary);
            Texts(AgentStrings.ClaudeLimitsNoApp, failure);
            ButtonAt(AgentStrings.GetClaude, () => UiKit.OpenUrl(AgentStrings.GetClaudeUrl));
        }
        else
        {
            Icon("", ThemeKeys.Orange);
            Texts(checkedAt is { } old ? AgentStrings.ClaudeLimitsStale(Relative(old, now)) : null, AgentStrings.ClaudeLimitsMenuBar, failure);
            if (_appInstalled)
            {
                ButtonAt(AgentStrings.OpenClaude, OpenClaude);
            }
        }

        var panel = new StackPanel { Children = { row } };
        if (status.ClaudeAccountChecking)
        {
            var spinner = new ProgressBar { IsIndeterminate = true, Height = 3, Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(spinner);
        }

        Child = panel;
    }

    /// <summary>Launches the Claude desktop app: its Start menu entry (MSIX), else its classic install folder, else the website.</summary>
    private static void OpenClaude()
    {
        var attempts = new List<Func<bool>>
        {
            () => Start("explorer.exe", "shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude"),
            () => Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicClaude", "claude.exe"), ""),
        };
        foreach (var attempt in attempts)
        {
            if (attempt())
            {
                return;
            }
        }

        UiKit.OpenUrl(AgentStrings.GetClaudeUrl);
    }

    private static bool Start(string file, string args)
    {
        try
        {
            if (!file.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && !File.Exists(file))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
