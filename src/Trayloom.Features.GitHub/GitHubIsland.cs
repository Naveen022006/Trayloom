// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trayloom.Core;
using L = Trayloom.Features.GitHub.GitHubLook;

namespace Trayloom.Features.GitHub;

/// <summary>The "GitHub" page of the expanded island (spec 06 section 2.7).</summary>
public sealed class GitHubIslandSection : IIslandSection
{
    public string? FeatureId => GhText.FeatureId;

    public int Order => 160;

    public string Id => "github";

    public string Title => GhText.Title;

    public string Glyph => "";

    public FrameworkElement CreateView(IIslandContext context)
    {
        if (context.Services.Get<GitHubRuntime>() is not { } runtime)
        {
            return GitHubPage.Message(GhText.Summary);
        }

        runtime.IslandContext = context;
        return new GitHubPage(runtime);
    }
}

/// <summary>The "github.notice" strip: a notice queued by a poll, shown once on the closed island.</summary>
public sealed class GitHubNoticeStrip : IIslandStrip
{
    public string? FeatureId => GhText.FeatureId;

    public int Order => 42;

    public string Id => "github.notice";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<GitHubRuntime>() is not { } runtime)
        {
            return null;
        }

        runtime.IslandContext = context;
        if (runtime.TakeNotice() is not { } notice)
        {
            return null;
        }

        var tint = L.Of(notice.Tint);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = L.Icon(notice.Glyph, 12, tint);
        icon.Margin = new Thickness(2, 0, 4, 0);
        panel.Children.Add(icon);
        var title = L.Text(notice.Title, 11, FontWeights.Medium, L.White(1), true, true);
        title.Margin = new Thickness(4, 0, 8, 0);
        title.MaxWidth = 170;
        panel.Children.Add(title);
        var detail = L.Text(notice.Detail, 11, FontWeights.Medium, tint, true, true);
        detail.MaxWidth = 120;
        panel.Children.Add(detail);
        return panel;
    }
}

/// <summary>The page itself: tiles, the list of the selected tile and a footer. It redraws when the runtime changes.</summary>
internal sealed class GitHubPage : StackPanel
{
    private const int UnreadTile = 0;
    private const int MineTile = 1;
    private const int ReviewTile = 2;
    private const int FailingTile = 3;

    private readonly GitHubRuntime _runtime;
    private int _selected = -1;

    public GitHubPage(GitHubRuntime runtime)
    {
        _runtime = runtime;
        Margin = new Thickness(2, 0, 2, 0);
        Build();
        Loaded += (_, _) =>
        {
            runtime.Changed += OnChanged;
            runtime.OnPageOpened();
            Build();
        };
        Unloaded += (_, _) => runtime.Changed -= OnChanged;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Build);

    /// <summary>A short centred message (the runtime is not running).</summary>
    public static FrameworkElement Message(string text) =>
        new TextBlock { Text = text, Foreground = L.White(0.6), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };

    private void Build()
    {
        Children.Clear();
        if (!_runtime.Connected)
        {
            BuildNotConnected();
            return;
        }

        var snapshot = _runtime.Snapshot ?? new GitHubSnapshot { Login = _runtime.Login };
        if (_selected < 0)
        {
            _selected = _runtime.SelectedTile >= 0 ? _runtime.SelectedTile : snapshot.FailingCount > 0 ? FailingTile : UnreadTile;
        }

        Children.Add(BuildTiles(snapshot));
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(list, "GitHub.List");
        BuildList(list, snapshot);
        Children.Add(list);
        Children.Add(BuildFooter(snapshot));
    }

    private void BuildNotConnected()
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 14, 0, 14) };
        var glyph = L.Icon("", 22, L.White(0.5));
        glyph.Margin = new Thickness(0, 0, 0, 8);
        box.Children.Add(glyph);
        var text = L.Text(GhText.ConnectPrompt, 13, null, L.White(1), false, false, true);
        text.MaxWidth = 360;
        text.TextAlignment = TextAlignment.Center;
        box.Children.Add(text);
        var caption = L.Text(GhText.NothingSent, 11, null, L.White(0.6));
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 4, 0, 10);
        box.Children.Add(caption);
        var pill = L.Pill(GhText.ConnectButton, () => _runtime.Services.Shell.OpenSettings("island#github"), "GitHub.Connect");
        pill.HorizontalAlignment = HorizontalAlignment.Center;
        box.Children.Add(pill);
        Children.Add(box);
    }

    private FrameworkElement BuildTiles(GitHubSnapshot s)
    {
        var grid = new UniformGrid { Columns = 4, Height = 58 };
        var unread = !s.NotificationsAvailable ? "–" : s.Notifications.Count >= 50 ? "50+" : s.Notifications.Count.ToString();
        grid.Children.Add(Tile(UnreadTile, GhText.TileUnread, unread, null, "GitHub.TileUnread"));
        grid.Children.Add(Tile(MineTile, GhText.TileMine, s.MineCount.ToString(), null, "GitHub.TileMine"));
        grid.Children.Add(Tile(ReviewTile, GhText.TileReview, s.ReviewCount.ToString(), s.ReviewCount > 0 ? L.Amber : null, "GitHub.TileReview"));
        grid.Children.Add(Tile(FailingTile, GhText.TileFailing, s.FailingCount.ToString(), s.FailingCount > 0 ? L.Red : null, "GitHub.TileFailing"));
        return grid;
    }

    private FrameworkElement Tile(int index, string label, string number, Color? tint, string id)
    {
        var selected = _selected == index;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var n = L.Text(number, 20, FontWeights.SemiBold, tint is { } t ? L.Of(t) : L.White(1), false, true);
        n.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(n);
        var l = L.Text(label, 10.5, null, L.White(0.6));
        l.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(l);
        var button = L.Flat(stack, () =>
        {
            _selected = index;
            _runtime.SelectedTile = index;
            Build();
        }, L.White(selected ? 0.16 : 0.08), L.White(selected ? 0.2 : 0.12), new CornerRadius(12), new Thickness(4), id, label, selected ? L.White(0.3) : null);
        button.Margin = new Thickness(index == 0 ? 0 : 3, 0, index == 3 ? 0 : 3, 0);
        return button;
    }

    private void BuildList(StackPanel list, GitHubSnapshot s)
    {
        switch (_selected)
        {
            case UnreadTile:
                if (!s.NotificationsAvailable)
                {
                    list.Children.Add(Empty(GhText.NeedsClassic));
                }
                else if (s.Notifications.Count == 0)
                {
                    list.Children.Add(Empty(GhText.NoUnread));
                }
                else
                {
                    foreach (var n in s.Notifications.Take(20))
                    {
                        var url = GitHubLinks.FromApi(n.SubjectUrl);
                        var when = n.UpdatedAt is { } u ? GitHubTime.Relative(u) : "";
                        list.Children.Add(ListRow(L.Icon(TypeGlyph(n.Type), 13, L.White(0.6)), n.Title, GhText.RepoCaption(n.Repo, when), () => GitHubLinks.Open(url)));
                    }
                }

                break;
            case MineTile:
                BuildPulls(list, s.Mine, s.MineCount, GhText.NoPulls, "https://github.com/pulls");
                break;
            case ReviewTile:
                BuildPulls(list, s.Review, s.ReviewCount, GhText.NoReviews, "https://github.com/pulls/review-requested");
                break;
            default:
                if (s.Repos.Count == 0)
                {
                    list.Children.Add(Empty(GhText.NoRepos));
                    break;
                }

                if (s.Repos.All(r => r.State == CheckState.Passing))
                {
                    var ok = L.Text(GhText.AllPass, 11, null, L.White(0.6));
                    ok.Margin = new Thickness(6, 0, 0, 4);
                    list.Children.Add(ok);
                }

                foreach (var r in s.Repos)
                {
                    list.Children.Add(RepoRow(r));
                }

                break;
        }
    }

    private void BuildPulls(StackPanel list, List<GhPull> pulls, int count, string empty, string allUrl)
    {
        if (pulls.Count == 0)
        {
            list.Children.Add(Empty(empty));
            return;
        }

        foreach (var p in pulls)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = L.Of(L.StateColor(p.State)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var caption = $"{p.Repo} #{p.Number}";
            if (p.IsDraft)
            {
                caption += " · " + GhText.Draft;
            }
            else if (p.ReviewDecision == "APPROVED")
            {
                caption += " · " + GhText.Approved;
            }
            else if (p.ReviewDecision == "CHANGES_REQUESTED")
            {
                caption += " · " + GhText.ChangesRequested;
            }
            else if (p.ReviewDecision == "REVIEW_REQUIRED")
            {
                caption += " · " + GhText.ReviewRequired;
            }

            var url = p.Url;
            list.Children.Add(ListRow(dot, p.Title, caption, () => GitHubLinks.Open(url)));
        }

        if (count > pulls.Count)
        {
            var more = L.Text(GhText.ShowAll, 12, FontWeights.Medium, L.White(0.9));
            var row = L.Row(more, () => GitHubLinks.Open(allUrl), "GitHub.ShowAll", GhText.ShowAll);
            row.Height = 34;
            list.Children.Add(row);
        }
    }

    private FrameworkElement RepoRow(GhRepo r)
    {
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = L.Of(L.StateColor(r.State)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        string caption;
        if (!r.Found)
        {
            caption = GhText.NotFound;
        }
        else
        {
            caption = r.State switch
            {
                CheckState.Passing => GhText.Passing,
                CheckState.Failing => GhText.FailingWord,
                CheckState.Running => GhText.Running,
                _ => GhText.NoChecks,
            };
            if (r.Release?.PublishedAt is { } published)
            {
                caption += GhText.ReleasedCaption(r.Release.Tag, GitHubTime.Short(published));
            }
        }

        var url = r.Url is null ? null : r.State is CheckState.Failing or CheckState.Running ? r.Url + "/actions" : r.Url;
        return ListRow(dot, r.Name, caption, () => GitHubLinks.Open(url), r.Found ? 0.6 : 0.32);
    }

    private static FrameworkElement ListRow(UIElement leading, string title, string caption, Action onClick, double captionOpacity = 0.6)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leading, 0);
        grid.Children.Add(leading);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(L.Text(title, 12.5, null, L.White(1), true));
        text.Children.Add(L.Text(caption, 11, null, L.White(captionOpacity), true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var row = L.Row(grid, onClick, "GitHub.Row", title);
        row.Height = 34;
        return row;
    }

    private static TextBlock Empty(string text)
    {
        var block = L.Text(text, 12, null, L.White(0.6));
        block.Margin = new Thickness(6, 8, 0, 8);
        return block;
    }

    /// <summary>Notification type glyphs (Segoe Fluent Icons): pull request, issue, release, discussion, other.</summary>
    private static string TypeGlyph(string type) => type switch
    {
        "PullRequest" => "",
        "Issue" => "",
        "Release" => "",
        "Discussion" => "",
        _ => "",
    };

    private FrameworkElement BuildFooter(GitHubSnapshot s)
    {
        var grid = new Grid { Height = 24, Margin = new Thickness(6, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fetched = s.FetchedAt.Year > 1;
        var error = ErrorText(s, fetched, _runtime.NextPollUtc);
        var status = error is not null
            ? L.Text(error, 11, null, L.Of(L.Amber), true)
            : L.Text(fetched ? GhText.Footer(_runtime.Login, GitHubTime.Relative(s.FetchedAt)) : "@" + _runtime.Login, 11, null, L.White(0.6), true);
        AutomationProperties.SetAutomationId(status, "GitHub.Status");
        AutomationProperties.SetName(status, status.Text);
        Grid.SetColumn(status, 0);
        grid.Children.Add(status);
        var col = 1;
        if (s.Error == GhError.SignedOut)
        {
            var again = L.Pill(GhText.TryAgain, () => _runtime.RequestRefresh(), "GitHub.TryAgain");
            again.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(again, col++);
            grid.Children.Add(again);
        }
        else if (s.Error == GhError.TokenRejected)
        {
            var settings = L.Pill(GhText.SettingsButton, () => _runtime.Services.Shell.OpenSettings("island#github"), "GitHub.Settings");
            settings.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(settings, col++);
            grid.Children.Add(settings);
        }

        var spin = L.Icon("", 12, L.White(0.8));
        spin.RenderTransformOrigin = new Point(0.5, 0.5);
        var rotate = new RotateTransform();
        spin.RenderTransform = rotate;
        if (_runtime.IsPolling)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        var refresh = L.Flat(spin, () => _runtime.RequestRefresh(), Brushes.Transparent, L.White(0.1), new CornerRadius(6), new Thickness(6, 2, 6, 2), "GitHub.Refresh", GhText.Refresh);
        L.Tip(refresh, GhText.Refresh);
        Grid.SetColumn(refresh, col++);
        grid.Children.Add(refresh);
        var openUrl = _selected == UnreadTile ? GitHubLinks.Notifications : "https://github.com";
        var open = L.Flat(L.Text(GhText.OpenGitHub, 11, FontWeights.Medium, L.White(0.9)), () => GitHubLinks.Open(openUrl), Brushes.Transparent, L.White(0.1), new CornerRadius(6), new Thickness(8, 2, 6, 2), "GitHub.Open", GhText.OpenGitHub);
        Grid.SetColumn(open, col);
        grid.Children.Add(open);
        return grid;
    }

    internal static string? ErrorText(GitHubSnapshot s, bool fetched, DateTime? next) => s.Error switch
    {
        GhError.None => null,
        GhError.Offline => GhText.ErrOffline(fetched ? GitHubTime.Clock(s.FetchedAt) : "–"),
        GhError.SignedOut => GhText.ErrSignedOut,
        GhError.TokenRejected => GhText.ErrTokenRejected,
        GhError.RateLimited => GhText.ErrRateLimited(s.RetryAt is { } r ? GitHubTime.Clock(r) : "–"),
        GhError.GhMissing => GhText.ErrGhMissing,
        _ => GhText.ErrUnexpected(GitHubTime.Clock(next ?? DateTime.UtcNow.AddMinutes(5))),
    };
}
