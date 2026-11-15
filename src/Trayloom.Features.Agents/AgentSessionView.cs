// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>The "Now" card: one row for every live session, with what it is doing and whether it waits for the person.</summary>
internal static class AgentSessionCard
{
    public const int MaxRows = 4;
    private const double RowHeight = 34;

    /// <summary>A row that also shows its subagents has a third line.</summary>
    private const double TallRowHeight = 46;

    /// <summary>Every session of the providers the card shows, idle ones of any age included.</summary>
    private static List<AgentSession> Providered(AgentCardContext c) =>
        c.Runtime.Sessions.Current.Where(s => c.Providers.Contains(s.Provider)).ToList();

    /// <summary>The sessions the card lists: all that work or wait, and idle ones only while they moved recently.</summary>
    public static List<AgentSession> Visible(AgentCardContext c) => AgentSessionSummary.Visible(Providered(c), c.Now);

    private static double HeightOf(AgentSession session) => session.Agents > 0 ? TallRowHeight : RowHeight;

    /// <summary>How many idle sessions are left out of the list because they have been quiet for a while.</summary>
    private static int HiddenIdle(AgentCardContext c) => Providered(c).Count - Visible(c).Count;

    /// <summary>Card height for the rows it will show.</summary>
    public static double Height(AgentCardContext c)
    {
        var sessions = Visible(c);
        var rows = sessions.Take(MaxRows).Sum(s => HeightOf(s) + 5);
        if (sessions.Count == 0)
        {
            rows = RowHeight + 5;
        }

        return 20 + 17 + 7 + rows + (sessions.Count > MaxRows ? 16 : 0) + (HiddenIdle(c) > 0 ? 16 : 0);
    }

    public static FrameworkElement Build(AgentCardContext c)
    {
        var sessions = Visible(c);
        var hiddenIdle = HiddenIdle(c);
        var waiting = sessions.Count(s => s.IsWaiting);
        var working = sessions.Count(s => s.State == AgentSessionState.Working);
        var accessory = new StackPanel { Orientation = Orientation.Horizontal };
        if (waiting > 0)
        {
            accessory.Children.Add(AU.Chip(waiting == 1 ? Loc.T("Waiting for you") : Loc.F("{0} waiting for you", waiting), AgentColors.Orange));
        }
        else if (working > 0)
        {
            accessory.Children.Add(AU.Chip(working == 1 ? Loc.T("1 working") : Loc.F("{0} working", working), AgentColors.Green));
        }

        if (sessions.Count > 0)
        {
            var lead = sessions[0];
            accessory.Children.Add(new AgentPulse(lead.IsWaiting ? AgentColors.Orange : lead.Provider.Tint(), 5) { Margin = new Thickness(4, 0, 0, 0) });
        }

        var body = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };
        FrameworkElement? claudeMark = null;
        if (sessions.Any(s => s.Provider == AgentProvider.Claude))
        {
            var claude = sessions.Where(s => s.Provider == AgentProvider.Claude).ToList();
            var mood = claude.Any(s => s.IsWaiting) ? MascotMood.Waiting : claude.Any(s => s.State == AgentSessionState.Working) ? MascotMood.Working : MascotMood.Idle;
            claudeMark = AgentSymbol.Create(AgentProvider.Claude, 8, mood, c.Runtime.Settings.ClaudeMascot());
            claudeMark.HorizontalAlignment = HorizontalAlignment.Center;
        }

        body.Children.Add(AgentCardViews.Header(AgentStrings.LiveCard, AgentCard.Live.Glyph(), waiting > 0 ? AgentColors.Orange : sessions.Count > 0 ? sessions[0].Provider.Tint() : null, null, accessory, claudeMark));
        var rows = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        var clocks = new List<(TextBlock Block, DateTime Started, string Prefix)>();
        if (sessions.Count == 0)
        {
            rows.Children.Add(Empty(c));
        }
        else
        {
            var first = true;
            foreach (var session in sessions.Take(MaxRows))
            {
                var row = Row(c, session, clocks);
                if (!first)
                {
                    row.Margin = new Thickness(0, 5, 0, 0);
                }

                first = false;
                rows.Children.Add(row);
            }

            if (sessions.Count > MaxRows)
            {
                var more = AU.Text(Loc.F("+{0} more", sessions.Count - MaxRows), 9.5, null, AgentColors.Tertiary);
                more.Margin = new Thickness(22, 4, 0, 0);
                rows.Children.Add(more);
            }
        }

        if (hiddenIdle > 0)
        {
            var idle = AU.Text(Loc.F("{0} idle", hiddenIdle), 9.5, null, AgentColors.Tertiary);
            idle.Margin = new Thickness(22, 4, 0, 0);
            rows.Children.Add(idle);
        }

        body.Children.Add(rows);
        if (clocks.Count > 0)
        {
            body.Loaded += (_, _) =>
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, _) =>
                {
                    foreach (var (block, started, prefix) in clocks)
                    {
                        block.Text = prefix + AgentFormat.Clock((DateTime.UtcNow - started).TotalSeconds);
                    }
                };
                timer.Start();
                body.Unloaded += (_, _) => timer.Stop();
            };
        }

        return AgentCardViews.Chrome(body);
    }

    private static FrameworkElement Empty(AgentCardContext c)
    {
        var line = c.Providers.Select(p => c.Snapshot.LastActivity.TryGetValue(p, out var d) ? p.DisplayName() + " " + AgentFormat.Relative(d, c.Now, false) : null).Where(s => s is not null);
        var text = line.Any() ? Loc.F("No live sessions · last active: {0}", string.Join(", ", line)) : Loc.T("No live sessions");
        var t = AU.Text(text, 10.5, null, AgentColors.Secondary, TextTrimming.CharacterEllipsis);
        t.Height = RowHeight - 6;
        return t;
    }

    private static FrameworkElement Row(AgentCardContext c, AgentSession session, List<(TextBlock, DateTime, string)> clocks)
    {
        var amber = AgentColors.Of(AgentColors.Orange);
        var grid = new Grid { Height = HeightOf(session), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mood = session.IsWaiting && session.Provider == AgentProvider.Claude ? MascotMood.Waiting : session.State == AgentSessionState.Working ? MascotMood.Working : MascotMood.Idle;
        var glyph = AgentSymbol.Create(session.Provider, 9, mood, c.Runtime.Settings.ClaudeMascot());
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(glyph);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var name = AU.Text(session.Project.Length == 0 ? session.Provider.DisplayName() : session.Project, 11, FontWeights.SemiBold, AgentColors.Primary, TextTrimming.CharacterEllipsis);
        name.MaxWidth = 190;
        top.Children.Add(name);
        text.Children.Add(top);
        var detail = session.Activity + (session.Origin.Length > 0 && session.State != AgentSessionState.Idle ? " · " + session.Origin : "");
        text.Children.Add(AU.Text(detail, 9.5, null, session.IsWaiting ? amber : AgentColors.Secondary, TextTrimming.CharacterEllipsis));
        if (session.Agents > 0)
        {
            var agents = AU.Text(AgentSessionSummary.SubagentLine(session), 9, null, AgentColors.Tertiary, TextTrimming.CharacterEllipsis);
            AU.Tip(agents, Loc.T("Subagents working for this session right now"));
            text.Children.Add(agents);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
        switch (session.State)
        {
            case AgentSessionState.Waiting:
            {
                var pill = AU.Chip(Loc.T("Waiting for you"), AgentColors.Orange);
                pill.HorizontalAlignment = HorizontalAlignment.Right;
                right.Children.Add(pill);
                var clock = AU.Text("", 9.5, FontWeights.Medium, amber, mono: true);
                clock.HorizontalAlignment = HorizontalAlignment.Right;
                clock.Text = AgentFormat.Clock((c.Now - session.Since).TotalSeconds);
                clocks.Add((clock, session.Since, ""));
                right.Children.Add(clock);
                break;
            }

            case AgentSessionState.Working:
            {
                var clock = AU.Text("", 11, FontWeights.Medium, session.Provider.TintBrush(), mono: true);
                clock.HorizontalAlignment = HorizontalAlignment.Right;
                clock.Text = AgentFormat.Clock((c.Now - session.Since).TotalSeconds);
                clocks.Add((clock, session.Since, ""));
                right.Children.Add(clock);
                right.Children.Add(AU.Text(Loc.T("Working"), 9, null, AgentColors.Secondary));
                ((TextBlock)right.Children[1]).HorizontalAlignment = HorizontalAlignment.Right;
                break;
            }

            default:
            {
                var idle = AU.Text(Loc.T("Idle"), 10.5, FontWeights.Medium, AgentColors.Secondary);
                idle.HorizontalAlignment = HorizontalAlignment.Right;
                right.Children.Add(idle);
                var since = AU.Text(AgentFormat.Relative(session.LastActivity, c.Now, false), 9, null, AgentColors.Tertiary);
                since.HorizontalAlignment = HorizontalAlignment.Right;
                right.Children.Add(since);
                break;
            }
        }

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var tip = new List<string> { session.Provider.DisplayName(), session.Origin };
        if (session.Title is { Length: > 0 } title)
        {
            tip.Add("\"" + title + "\"");
        }

        tip.Add(session.State switch
        {
            AgentSessionState.Waiting => session.Basis switch
            {
                WaitingBasis.Hook => "waiting for you (reported by the Trayloom hook)",
                WaitingBasis.Status => "waiting for you (reported by the session)",
                WaitingBasis.Approval => "waiting for your approval (logged by Codex)",
                _ => "probably waiting for a permission prompt (a tool call has had no result for a while)",
            },
            AgentSessionState.Working => "working",
            _ => "idle",
        });
        AU.Tip(grid, string.Join(" · ", tip.Where(t => t.Length > 0)));
        if (session.IsWaiting)
        {
            grid.Cursor = Cursors.Hand;
            grid.MouseLeftButtonUp += (_, e) =>
            {
                c.Runtime.FocusSession(new AgentAttention(session.Key, session.Provider, session.Project, session.Tool, "", session.Basis, session.Id, session.Pid, session.Origin, DateTime.UtcNow));
                e.Handled = true;
            };
        }

        return grid;
    }
}

/// <summary>The closed-island notice "Claude needs your permission": the mark, the words, and the project and tool in amber.</summary>
public sealed class AgentAttentionView : StackPanel
{
    public AgentAttentionView(AgentAttention attention, Action? onClick = null, bool mascot = false)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        var amber = AgentColors.Orange;
        Children.Add(new AgentPulse(amber, 5) { Margin = new Thickness(0, 0, 2, 0) });
        Children.Add(AgentSymbol.Create(attention.Provider, 11, MascotMood.Waiting, mascot));
        var title = AU.Text(attention.Title, 11, FontWeights.Medium, AgentColors.Primary, TextTrimming.CharacterEllipsis, false, true);
        title.Margin = new Thickness(4, 0, 8, 0);
        title.MaxWidth = 160;
        Children.Add(title);
        var detail = AU.Text(attention.Project + (attention.Tool is { Length: > 0 } tool ? " · " + tool : ""), 11, FontWeights.Medium, AgentColors.Of(amber), TextTrimming.CharacterEllipsis, false, true);
        detail.MaxWidth = 130;
        AU.Tip(this, attention.Title + " — " + attention.Project + (attention.Tool is { Length: > 0 } tl ? ": " + tl : "") + ". Click to bring its window forward.");
        Children.Add(detail);
        Cursor = Cursors.Hand;
        if (onClick is not null)
        {
            // The island reads Tag as the action of a click on this notice (it brings the right window forward).
            Tag = onClick;
        }
    }
}
