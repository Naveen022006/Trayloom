// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Trayloom.Core;
using L = Trayloom.Features.GitHub.GitHubLook;

namespace Trayloom.Features.GitHub;

/// <summary>
/// The "github.coding" strip: branch, changes and checks of the repository you are working in, on the closed island. It yields to music,
/// timers and agents, which the island ranks higher, and a click opens the GitHub page (the island maps "github.coding" to "github").
/// </summary>
public sealed class GitHubCodingStrip : IIslandStrip
{
    public string? FeatureId => GhText.FeatureId;

    public int Order => 45;

    public string Id => "github.coding";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<CodingTracker>() is not { } tracker)
        {
            return null;
        }

        // The context is kept even when there is nothing to show yet: it is how the tracker asks for the strip once there is.
        tracker.IslandContext = context;
        if (!tracker.IsOn || !context.App.Get<bool>(CodingKeys.Enabled) || !context.App.Get<bool>(CodingKeys.Strip))
        {
            return null;
        }

        var snapshot = tracker.Snapshot;
        return snapshot.Repo is null || snapshot.Git is null ? null : new CodingStripView(tracker);
    }
}

/// <summary>The row of the strip. It keeps its texts up to date while it is on screen.</summary>
internal sealed class CodingStripView : StackPanel
{
    private readonly CodingTracker _tracker;
    private readonly TextBlock _branch;
    private readonly TextBlock _changes;
    private readonly TextBlock _ahead;
    private readonly TextBlock _behind;
    private readonly CheckDot _dot = new();

    public CodingStripView(CodingTracker tracker)
    {
        _tracker = tracker;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        MaxWidth = 200;
        AutomationProperties.SetAutomationId(this, "GitHub.CodingStrip");
        var icon = L.Icon("", 12, L.White(0.8));
        icon.Margin = new Thickness(2, 0, 5, 0);
        Children.Add(icon);
        _branch = L.Text("", 11, FontWeights.SemiBold, L.White(1), true);
        _branch.MaxWidth = 110;
        Children.Add(_branch);
        _changes = Counter();
        _ahead = Counter();
        _behind = Counter();
        Children.Add(_changes);
        Children.Add(_ahead);
        Children.Add(_behind);
        _dot.Margin = new Thickness(7, 0, 2, 0);
        Children.Add(_dot);
        Update();
        Loaded += (_, _) =>
        {
            tracker.Changed += OnChanged;
            Update();
            _dot.Resume();
        };
        Unloaded += (_, _) =>
        {
            tracker.Changed -= OnChanged;
            _dot.Pause();
        };
    }

    private static TextBlock Counter()
    {
        var text = L.Text("", 11, FontWeights.Normal, L.White(0.6), false, true);
        text.Margin = new Thickness(6, 0, 0, 0);
        return text;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Update);

    private void Update()
    {
        var s = _tracker.Snapshot;
        if (s.Git is not { } g)
        {
            return;
        }

        _branch.Text = s.BranchLabel ?? "";
        Show(_changes, g.Changes > 0 ? "±" + g.Changes : null);
        Show(_ahead, g.Ahead > 0 ? "↑" + g.Ahead : null);
        Show(_behind, g.Behind > 0 ? "↓" + g.Behind : null);
        _dot.Set(s.Checks?.State ?? CheckState.None);
        L.Tip(this, CodingLines.Tooltip(s));
    }

    private static void Show(TextBlock block, string? text)
    {
        block.Text = text ?? "";
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>A 6 px dot in the colour of the checks; it pulses while they run, unless Windows animation effects are off.</summary>
internal sealed class CheckDot : Grid
{
    private readonly Ellipse _fill = new() { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _ring = new() { Width = 6, Height = 6, StrokeThickness = 1, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScaleTransform _scale = new(1, 1);
    private CheckState _state = CheckState.None;
    private bool _paused;

    public CheckDot()
    {
        Width = 12;
        Height = 12;
        IsHitTestVisible = false;
        _ring.RenderTransform = _scale;
        Children.Add(_fill);
        Children.Add(_ring);
        Visibility = Visibility.Collapsed;
    }

    public void Set(CheckState state)
    {
        _state = state;
        Visibility = state == CheckState.None ? Visibility.Collapsed : Visibility.Visible;
        var color = L.StateColor(state);
        _fill.Fill = L.Of(color);
        _ring.Stroke = L.Of(color);
        Restart();
    }

    public void Pause()
    {
        _paused = true;
        Restart();
    }

    public void Resume()
    {
        _paused = false;
        Restart();
    }

    private void Restart()
    {
        _ring.BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _ring.Opacity = 0;
        if (_state != CheckState.Running || _paused || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var duration = TimeSpan.FromSeconds(1.6);
        var forever = RepeatBehavior.Forever;
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 0, duration) { RepeatBehavior = forever });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.9, duration) { RepeatBehavior = forever });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.9, duration) { RepeatBehavior = forever });
    }
}

/// <summary>The lines of the strip tooltip, the card and the settings status, built from one snapshot.</summary>
internal static class CodingLines
{
    public static string ChecksText(CheckSummary c) => c.State switch
    {
        CheckState.Running => CodingText.ChecksRunning(Math.Max(0, c.Total - c.Running), c.Total),
        CheckState.Passing => c.Total > 0 ? CodingText.AllPassed(c.Total) : CodingText.ChecksPassed,
        CheckState.Failing => c.Failed > 0 ? CodingText.Failing(c.Failed, string.Join(", ", c.Failing)) : GhText.NoticeChecks,
        _ => CodingText.NoChecks,
    };

    public static string Tooltip(CodingSnapshot s)
    {
        var parts = new List<string>();
        if (s.Repo is not null)
        {
            parts.Add(s.Repo.Name);
        }

        if (s.BranchLabel is { } branch)
        {
            parts.Add(branch);
        }

        if (s.Git is { } g)
        {
            parts.Add(g.Changes > 0 ? CodingText.Changed(g.Changes) : CodingText.NoChanges);
            if (g.Ahead > 0)
            {
                parts.Add(CodingText.ToPush(g.Ahead));
            }

            if (g.Behind > 0)
            {
                parts.Add(CodingText.ToPull(g.Behind));
            }
        }

        if (s.Checks is { } checks)
        {
            parts.Add(ChecksText(checks));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The second line of the card: changes and what is left to push or pull, or the reason Git gave nothing.</summary>
    public static string? StateLine(CodingSnapshot s)
    {
        if (CodingText.GitErrorText(s.GitError) is { } error)
        {
            return error;
        }

        if (s.Git is not { } g)
        {
            return null;
        }

        var line = g.Changes > 0 ? CodingText.Changed(g.Changes) : CodingText.NoChanges;
        if (g.Upstream is null)
        {
            return g.Detached ? line : line + " · " + CodingText.NotPushed;
        }

        if (g.Ahead > 0)
        {
            line += " · " + CodingText.ToPush(g.Ahead);
        }

        if (g.Behind > 0)
        {
            line += " · " + CodingText.ToPull(g.Behind);
        }

        return line;
    }

    public static string PullLine(BranchPull p)
    {
        var line = CodingText.PullTitle(p.Number, p.Title);
        if (p.IsDraft)
        {
            return line + " · " + GhText.Draft;
        }

        return p.ReviewDecision switch
        {
            "APPROVED" => line + " · " + GhText.Approved,
            "CHANGES_REQUESTED" => line + " · " + GhText.ChangesRequested,
            "REVIEW_REQUIRED" => line + " · " + GhText.ReviewRequired,
            _ => line,
        };
    }

    /// <summary>The settings status line.</summary>
    public static string Status(CodingSnapshot s) =>
        CodingText.GitErrorText(s.GitError) ?? (s.Repo is not null && s.BranchLabel is { } branch ? CodingText.WorkingIn(s.Repo.Name, branch) : CodingText.NotCoding);
}

/// <summary>The card on the GitHub page (spec 09 part 2): the repository, its branch, the pull request and the checks.</summary>
internal static class CodingIsland
{
    private static readonly Color Purple = Color.FromRgb(0x82, 0x50, 0xDF);

    public static FrameworkElement CodingCard(CodingTracker tracker, GitHubRuntime runtime)
    {
        var s = tracker.Snapshot;
        var card = new Border { Background = L.White(0.08), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(card, "GitHub.Coding.Card");
        var stack = new StackPanel();
        card.Child = stack;

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var name = L.Text(s.Repo?.Name ?? "", 13, FontWeights.SemiBold, L.White(1), true);
        name.MaxWidth = 240;
        AutomationProperties.SetAutomationId(name, "GitHub.Coding.Repo");
        head.Children.Add(name);
        if (s.BranchLabel is { } label)
        {
            var chip = new Border { Background = L.Of(Purple, 0.22), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var chipText = L.Text(label, 9, FontWeights.SemiBold, L.Of(Purple), true);
            chipText.MaxWidth = 150;
            AutomationProperties.SetAutomationId(chipText, "GitHub.Coding.Branch");
            chip.Child = chipText;
            head.Children.Add(chip);
        }

        stack.Children.Add(head);

        if (CodingLines.StateLine(s) is { } state)
        {
            var failed = s.GitError != GitError.None;
            var line = L.Text(state, 11, null, failed ? L.Of(L.Amber) : L.White(0.6), !failed, false, failed);
            line.Margin = new Thickness(0, 3, 0, 0);
            AutomationProperties.SetAutomationId(line, "GitHub.Coding.State");
            stack.Children.Add(line);
        }

        var showRemote = s.Slug is not null && s.Git is { Detached: false, Branch: not null };
        if (showRemote && !runtime.Connected)
        {
            var connect = L.Text(CodingText.ConnectLine, 11, null, L.White(0.6), false, false, true);
            connect.Margin = new Thickness(0, 3, 0, 0);
            AutomationProperties.SetAutomationId(connect, "GitHub.Coding.Connect");
            stack.Children.Add(connect);
        }
        else if (showRemote && s.GitHubAt is not null && s.GitHubFound)
        {
            var pull = L.Text(s.Pull is { } p ? CodingLines.PullLine(p) : CodingText.NoPull, 11, null, L.White(0.6), true);
            pull.Margin = new Thickness(0, 3, 0, 0);
            AutomationProperties.SetAutomationId(pull, "GitHub.Coding.Pull");
            stack.Children.Add(pull);
            var checks = s.Checks;
            var tint = checks?.State switch
            {
                CheckState.Failing => L.Of(L.Red),
                CheckState.Running => L.Of(L.Amber),
                _ => L.White(0.6),
            };
            var checkLine = L.Text(checks is null ? CodingText.NoChecks : CodingLines.ChecksText(checks), 11, null, tint, true);
            checkLine.Margin = new Thickness(0, 1, 0, 0);
            AutomationProperties.SetAutomationId(checkLine, "GitHub.Coding.Checks");
            stack.Children.Add(checkLine);
        }

        var pills = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (s.Pull is { } open)
        {
            pills.Children.Add(Pill(CodingText.OpenPull, open.Url, "GitHub.Coding.OpenPull"));
        }

        if (s.Checks?.Url is { } checksUrl)
        {
            pills.Children.Add(Pill(CodingText.OpenChecks, checksUrl, "GitHub.Coding.OpenChecks"));
        }

        if (CreatePullUrl(s) is { } create)
        {
            pills.Children.Add(Pill(CodingText.CreatePull, create, "GitHub.Coding.CreatePull"));
        }

        if (s.Slug is { } slug)
        {
            pills.Children.Add(Pill(CodingText.OpenRepo, $"https://github.com/{slug.Owner}/{slug.Name}", "GitHub.Coding.OpenRepo"));
        }

        if (pills.Children.Count > 0)
        {
            stack.Children.Add(pills);
        }

        return card;
    }

    /// <summary>
    /// The "compare" page for a branch that is pushed, has no pull request yet and is not the default branch. Only offered once GitHub
    /// has answered, so a pull request that exists is not hidden by a lookup that has not finished.
    /// </summary>
    internal static string? CreatePullUrl(CodingSnapshot s)
    {
        if (s.Slug is not { } slug || s.Git is not { Detached: false, Branch: { } branch, Upstream: not null } || s.Pull is not null || s.GitHubAt is null || !s.GitHubFound)
        {
            return null;
        }

        var isDefault = s.DefaultBranch is { } def ? string.Equals(def, branch, StringComparison.Ordinal) : branch is "main" or "master";
        return isDefault ? null : $"https://github.com/{slug.Owner}/{slug.Name}/compare/{Uri.EscapeDataString(branch)}?expand=1";
    }

    private static Button Pill(string text, string url, string id)
    {
        var pill = L.Pill(text, () => GitHubLinks.Open(url), id);
        pill.Margin = new Thickness(0, 0, 6, 4);
        return pill;
    }
}
