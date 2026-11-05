// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Agents;

/// <summary>
/// The "AI Agents" page of the expanded island (spec 03 A7.1): cards the person picked, paired across the width, scrolling when they do
/// not fit. Always dark (the island is black).
/// </summary>
public sealed class AgentsPageView : Grid, IIslandTopRowHost
{
    /// <summary>Height of the soft fade at the top and bottom scroll edges.</summary>
    private const double FadeHeight = 18;

    /// <summary>How long after the last wheel step a data-driven rebuild keeps waiting, so cards never change under the reader.</summary>
    private const double ScrollQuietMs = 600;

    private readonly AgentsRuntime _runtime;
    private readonly Border _topRow = new();
    private FrameworkElement? _bar;

    // The layout is built once: only the card grid inside _scroll is swapped, so the scroll viewer (and its offset) survives a rebuild.
    private readonly Grid _root = new();
    private readonly Border _barSlot = new();
    private readonly Grid _listHost = new();
    private readonly Trayloom.Core.Ui.SmoothScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Border _scrollIndicator = NewIndicator();
    private readonly DispatcherTimer _deferred = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly GradientStop _topEdge = new(Colors.Black, 0);
    private readonly GradientStop _topInner = new(Colors.Black, 0);
    private readonly GradientStop _bottomInner = new(Colors.Black, 1);
    private readonly GradientStop _bottomEdge = new(Colors.Black, 1);
    private LinearGradientBrush? _mask;
    private bool _dirty;
    private DateTime _lastScroll = DateTime.MinValue;
    private bool _topRowClaimed;
    private readonly bool _preview;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(15) };

    private static Border NewIndicator() => new() { Width = 3, CornerRadius = new CornerRadius(1.5), Background = AgentColors.White(0.28), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0) };
    private bool _subscribed;
    private readonly Action _onChanged;
    private Popup? _menu;

    /// <summary>Creates the page. <paramref name="preview"/> is true in the Settings preview (reads, but does not count as the page being shown).</summary>
    public AgentsPageView(AgentsRuntime runtime, bool preview = false)
    {
        _runtime = runtime;
        _preview = preview;
        _onChanged = () => Dispatcher.BeginInvoke(RequestRebuild);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        _refresh.Tick += (_, _) => RequestRebuild();
        _deferred.Tick += (_, _) => FlushRebuild();
        Background = Brushes.Transparent;

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_barSlot);
        _listHost.Children.Add(_scroll);
        _listHost.Children.Add(_scrollIndicator);
        SetRow(_listHost, 1);
        _root.Children.Add(_listHost);
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange != 0)
            {
                _lastScroll = DateTime.UtcNow;
            }

            UpdateIndicator();
        };
        _scroll.SizeChanged += (_, _) => UpdateIndicator();
        Rebuild();
    }

    /// <summary>Asks for a rebuild with fresh data; it runs once the reader has stopped scrolling and no menu is open.</summary>
    private void RequestRebuild()
    {
        _dirty = true;
        if (!_deferred.IsEnabled)
        {
            _deferred.Start();
        }
    }

    private void FlushRebuild()
    {
        if (!_dirty)
        {
            _deferred.Stop();
            return;
        }

        // Never swap the cards under a glide, right after a wheel step or under the open period menu: try again next tick.
        if (SmoothScroll.IsAnimating(_scroll) || (DateTime.UtcNow - _lastScroll).TotalMilliseconds < ScrollQuietMs || _menu?.IsOpen == true)
        {
            return;
        }

        _deferred.Stop();
        Rebuild();
    }

    /// <summary>The island's top row takes the period menu (it sits beside the back button, not under an empty band).</summary>
    public FrameworkElement ClaimTopRow()
    {
        if (!_topRowClaimed)
        {
            _topRowClaimed = true;
            if (_bar is not null)
            {
                _barSlot.Child = null;
                _bar.Margin = new Thickness(0);
                _topRow.Child = _bar;
            }
        }

        return _topRow;
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
        _runtime.SessionsChanged += _onChanged;
        _runtime.Sessions.SetWatching(true);
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
        _runtime.SessionsChanged -= _onChanged;
        _runtime.Sessions.SetWatching(false);
        _runtime.Services.App.Changed -= OnSetting;
        _refresh.Stop();
        _deferred.Stop();
        if (_menu is { IsOpen: true })
        {
            _menu.IsOpen = false;
        }

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
            Dispatcher.BeginInvoke(RequestRebuild);
        }
    }

    private void Rebuild()
    {
        var settings = _runtime.Settings;
        var snapshot = _runtime.Service.Snapshot;
        var accounts = settings.ShowsCodexAccounts() ? _runtime.Accounts.States : [];
        var status = _runtime.Service.Status;
        _dirty = false;
        _barSlot.Child = null;
        _topRow.Child = null;
        _bar = null;

        // Only agents that left something on this PC, or whose accounts were added, get cards.
        var providers = settings.Providers()
            .Where(p => snapshot.Seen.Contains(p) || (p == AgentProvider.Codex && accounts.Count > 0) || (p == AgentProvider.Claude && !status.ClaudeProfile.IsDefault)).ToList();

        if (!snapshot.Loaded && accounts.Count == 0)
        {
            ShowMessage(Loading());
            return;
        }

        if (providers.Count == 0)
        {
            ShowMessage(EmptyView("sparkles", AgentStrings.Empty));
            return;
        }

        var cards = settings.Cards();
        var tiles = AgentSettings.Tiles(cards, providers, accounts.Select(a => a.Id).ToList(), status.ClaudeProfile.IsDefault ? null : status.ClaudeProfile.Id);
        if (tiles.Count == 0)
        {
            ShowMessage(EmptyView("grid", AgentStrings.NoCards));
            return;
        }

        if (providers.Contains(AgentProvider.Codex))
        {
            // Codex's limits card shows the account's own answer, asked at most every few minutes while the page is open.
            _runtime.Resets.RefreshIfStale();
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

        // Cards that grow with what they show: the sessions listed, the limits and their notes.
        tiles = tiles.Select(t => t.Card switch
        {
            AgentCard.Live => t with { HeightHint = AgentSessionCard.Height(context) },
            AgentCard.Spend => t with { HeightHint = 146 },
            AgentCard.Limits when t.Provider is not null => t with { HeightHint = AgentCardViews.LimitsHeight(context, t) },
            _ => t,
        }).ToList();
        // The cards stop short of the right edge so the scroll indicator does not draw over them.
        var grid = new AgentCardGrid { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var tile in tiles)
        {
            var view = BuildCard(context, tile);
            AgentCardGrid.SetTile(view, tile);
            grid.Children.Add(view);
        }

        // The period drives several cards, so it sits in the island's top row while any of them shows (in the page itself when the
        // island did not claim the row, as in the Settings preview).
        if (tiles.Any(t => t.Card.FollowsPeriod()))
        {
            var bar = PeriodMenu(period);
            _bar = bar;
            if (_topRowClaimed)
            {
                _topRow.Child = bar;
            }
            else
            {
                bar.Margin = new Thickness(0, 0, 0, 8);
                _barSlot.Child = bar;
            }
        }

        // Swap only the cards: the viewer, its offset and any glide in flight stay, so a refresh never jumps the list to the top.
        var keep = _scroll.VerticalOffset;
        if (Children.Count != 1 || !ReferenceEquals(Children[0], _root))
        {
            Children.Clear();
            Children.Add(_root);
        }

        _scroll.Content = grid;
        // Synchronous: the offset is applied in the same layout pass, before anything is rendered with the new cards at the top.
        _scroll.ScrollToVerticalOffset(keep);
        Dispatcher.BeginInvoke(UpdateIndicator, DispatcherPriority.Loaded);
    }

    /// <summary>Shows a loading or empty message in place of the list and drops the stale cards.</summary>
    private void ShowMessage(UIElement element)
    {
        _scroll.Content = null;
        Children.Clear();
        Children.Add(element);
    }

    private void UpdateIndicator()
    {
        if (_scroll.ScrollableHeight <= 0.5)
        {
            _scrollIndicator.Visibility = Visibility.Collapsed;
            UpdateFade();
            return;
        }

        var viewport = _scroll.ViewportHeight;
        var content = _scroll.ExtentHeight;
        var height = Math.Max(24, viewport * viewport / content);
        _scrollIndicator.Height = height;
        _scrollIndicator.Margin = new Thickness(0, (viewport - height) * _scroll.VerticalOffset / _scroll.ScrollableHeight, 0, 0);
        _scrollIndicator.Visibility = Visibility.Visible;
        UpdateFade();
    }

    /// <summary>
    /// Fades the content out over the last few pixels at each edge it can still scroll towards, instead of cutting it off flat.
    /// An opacity mask on the viewer only, so the island's own clip and the other pages are untouched.
    /// </summary>
    private void UpdateFade()
    {
        var height = _scroll.ActualHeight;
        if (_scroll.ScrollableHeight <= 0.5 || height <= 1)
        {
            _scroll.OpacityMask = null;
            return;
        }

        _mask ??= new LinearGradientBrush(new GradientStopCollection { _topEdge, _topInner, _bottomInner, _bottomEdge }, new Point(0, 0), new Point(0, 1));
        var fraction = Math.Min(0.25, FadeHeight / height);
        _topInner.Offset = fraction;
        _bottomInner.Offset = 1 - fraction;
        _topEdge.Color = _scroll.VerticalOffset > 0.5 ? Colors.Transparent : Colors.Black;
        _bottomEdge.Color = _scroll.VerticalOffset < _scroll.ScrollableHeight - 0.5 ? Colors.Transparent : Colors.Black;
        _scroll.OpacityMask = _mask;
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
            ? Trayloom.Core.Ui.ClaudeMark.Create(30, AgentColors.Of(Trayloom.Core.Ui.ClaudeMark.Orange, 0.75))
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
