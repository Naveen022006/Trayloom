// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Agents;

/// <summary>
/// The "AI Agents" page of the expanded island (spec 03 A7.1): cards the person picked, paired across the width, scrolling when they do
/// not fit. Always dark (the island is black).
/// </summary>
public sealed class AgentsPageView : Grid
{
    private readonly AgentsRuntime _runtime;
    private readonly bool _preview;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(15) };
    private Border _scrollIndicator = NewIndicator();

    private static Border NewIndicator() => new() { Width = 3, CornerRadius = new CornerRadius(1.5), Background = AgentColors.White(0.28), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 1, 0) };
    private ScrollViewer? _scroll;
    private bool _subscribed;
    private readonly Action _onChanged;
    private Popup? _menu;

    /// <summary>Creates the page. <paramref name="preview"/> is true in the Settings preview (reads, but does not count as the page being shown).</summary>
    public AgentsPageView(AgentsRuntime runtime, bool preview = false)
    {
        _runtime = runtime;
        _preview = preview;
        _onChanged = () => Dispatcher.BeginInvoke(Rebuild);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        _refresh.Tick += (_, _) => Rebuild();
        Background = Brushes.Transparent;
        Rebuild();
    }

    private void Attach()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _runtime.Service.Changed += _onChanged;
        _runtime.Accounts.Changed += _onChanged;
        _runtime.Resets.Changed += _onChanged;
        _runtime.Services.App.Changed += OnSetting;
        _refresh.Start();
        _runtime.Service.PageDidAppear(_preview);
        _runtime.Accounts.PageDidAppear(_preview);
        Rebuild();
    }

    private void Detach()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        _runtime.Service.Changed -= _onChanged;
        _runtime.Accounts.Changed -= _onChanged;
        _runtime.Resets.Changed -= _onChanged;
        _runtime.Services.App.Changed -= OnSetting;
        _refresh.Stop();
        if (!_preview)
        {
            _runtime.Service.PageDidDisappear();
            _runtime.Accounts.PageDidDisappear();
        }
    }

    private void OnSetting(string key)
    {
        if (key.StartsWith("notchAgents", StringComparison.Ordinal))
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
    }

    private void Rebuild()
    {
        var settings = _runtime.Settings;
        var snapshot = _runtime.Service.Snapshot;
        var accounts = settings.ShowsCodexAccounts() ? _runtime.Accounts.States : [];
        var status = _runtime.Service.Status;
        Children.Clear();
        _scroll = null;
        _scrollIndicator = NewIndicator();

        // Only agents that left something on this PC, or whose accounts were added, get cards.
        var providers = settings.Providers()
            .Where(p => snapshot.Seen.Contains(p) || (p == AgentProvider.Codex && accounts.Count > 0) || (p == AgentProvider.Claude && !status.ClaudeProfile.IsDefault)).ToList();

        if (!snapshot.Loaded && accounts.Count == 0)
        {
            Children.Add(Loading());
            return;
        }

        if (providers.Count == 0)
        {
            Children.Add(EmptyView("sparkles", AgentStrings.Empty));
            return;
        }

        var cards = settings.Cards();
        var tiles = AgentSettings.Tiles(cards, providers, accounts.Select(a => a.Id).ToList(), status.ClaudeProfile.IsDefault ? null : status.ClaudeProfile.Id);
        if (tiles.Count == 0)
        {
            Children.Add(EmptyView("grid", AgentStrings.NoCards));
            return;
        }

        var period = settings.Period();
        var context = new AgentCardContext
        {
            Runtime = _runtime,
            Snapshot = snapshot,
            Now = DateTime.UtcNow,
            Providers = providers,
            Period = period,
            UsedDisplay = settings.UsedDisplay(),
            CodexAccounts = accounts,
            OpenAgentSettings = () => _runtime.Services.Shell.OpenSettings(SettingsPages.Join(SettingsPages.Island, AgentsSettingsSection.AnchorId)),
        };

        var grid = new AgentCardGrid();
        foreach (var tile in tiles)
        {
            var view = BuildCard(context, tile);
            AgentCardGrid.SetTile(view, tile);
            grid.Children.Add(view);
        }

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // The period drives several cards, so it sits in the island's top row while any of them shows.
        if (tiles.Any(t => t.Card.FollowsPeriod()))
        {
            var bar = PeriodMenu(period);
            bar.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(bar);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
            Focusable = false,
        };
        _scroll = scroll;
        scroll.ScrollChanged += (_, _) => UpdateIndicator();
        scroll.SizeChanged += (_, _) => UpdateIndicator();
        var host = new Grid { Children = { scroll, _scrollIndicator } };
        Grid.SetRow(host, 1);
        root.Children.Add(host);
        Children.Add(root);
        Dispatcher.BeginInvoke(UpdateIndicator, DispatcherPriority.Loaded);
    }

    private void UpdateIndicator()
    {
        if (_scroll is null || _scroll.ScrollableHeight <= 0.5)
        {
            _scrollIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var viewport = _scroll.ViewportHeight;
        var content = _scroll.ExtentHeight;
        var height = Math.Max(24, viewport * viewport / content);
        _scrollIndicator.Height = height;
        _scrollIndicator.Margin = new Thickness(0, (viewport - height) * _scroll.VerticalOffset / _scroll.ScrollableHeight, 1, 0);
        _scrollIndicator.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildCard(AgentCardContext c, AgentTile tile)
    {
        var usage = c.Snapshot.Usage(c.Period);
        switch (tile.Card)
        {
            case AgentCard.Limits:
                return AgentCardViews.Limits(c, tile);
            case AgentCard.Spend:
                return AgentCardViews.Spend(c);
            case AgentCard.Live:
                return AgentCardViews.Live(c);
            case AgentCard.Trend:
                return AgentCardViews.Trend(c);
            case AgentCard.Models:
                return AgentCardViews.Shares(c, AgentStrings.ModelsCard, AgentCard.Models.Glyph(), usage.Models, usage.FullyPriced);
            case AgentCard.Projects:
                return AgentCardViews.Shares(c, AgentStrings.ProjectsCard, AgentCard.Projects.Glyph(), usage.Projects, usage.FullyPriced);
            case AgentCard.Activity:
                return AgentCardViews.Activity(c);
            default:
                _runtime.Resets.RefreshIfStale();
                return AgentCardViews.Resets(c, tile, Rebuild);
        }
    }

    private static FrameworkElement Loading()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new AgentSpinner(16) { HorizontalAlignment = HorizontalAlignment.Center });
        var text = AU.Text(AgentStrings.Loading, 11, null, AgentColors.Secondary);
        text.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(text);
        return stack;
    }

    /// <summary>The island's empty page: a symbol and a centred message.</summary>
    internal static FrameworkElement EmptyView(string symbol, string message)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300 };
        FrameworkElement icon = symbol == "sparkles"
            ? AgentMarks.Sparkles(30, AgentColors.White(0.4))
            : AU.Icon("", 28, AgentColors.White(0.4));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = AU.Text(message, 12, null, AgentColors.Secondary);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextAlignment = TextAlignment.Center;
        text.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(text);
        return stack;
    }

    // ------------------------------------------------------------------------------------------ period menu

    private FrameworkElement PeriodMenu(AgentPeriod current)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(AU.Text(AgentStrings.Period(current), 11, FontWeights.Medium, AgentColors.Strong));
        var chevron = AU.Icon(Glyphs.ChevronDownSmall, 8, AgentColors.Secondary);
        chevron.Margin = new Thickness(5, 1, 0, 0);
        label.Children.Add(chevron);
        var button = new Button
        {
            Content = new Border { Child = label, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(9), Background = AgentColors.White(0.1) },
            Style = AgentButton.PlainStyle,
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => OpenMenu(button, current);
        return button;
    }

    private void OpenMenu(FrameworkElement anchor, AgentPeriod current)
    {
        _menu?.SetCurrentValue(Popup.IsOpenProperty, false);
        var panel = new StackPanel { Margin = new Thickness(4) };
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 4,
        };
        foreach (var option in Enum.GetValues<AgentPeriod>())
        {
            var row = new Grid { MinWidth = 120 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (option == current)
            {
                row.Children.Add(AU.Icon(Glyphs.Check, 10, AgentColors.Primary));
            }

            var text = AU.Text(AgentStrings.Period(option), 12, null, AgentColors.Primary);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var item = new Button
            {
                Content = new Border { Child = row, Padding = new Thickness(6, 5, 10, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent },
                Style = AgentButton.PlainStyle,
                Cursor = Cursors.Hand,
            };
            item.MouseEnter += (_, _) => ((Border)item.Content).Background = AgentColors.White(0.12);
            item.MouseLeave += (_, _) => ((Border)item.Content).Background = Brushes.Transparent;
            var chosen = option;
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                _runtime.Services.App.Set(AgentKeys.Period, chosen.Raw());
                Rebuild();
            };
            panel.Children.Add(item);
        }

        popup.Child = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x26)),
            BorderBrush = AgentColors.White(0.14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5 },
            Margin = new Thickness(12),
        };
        _menu = popup;
        popup.IsOpen = true;
    }
}
