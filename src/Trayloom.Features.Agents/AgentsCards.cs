// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core.Ui;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>What the cards of the AI page draw from.</summary>
internal sealed class AgentCardContext
{
    public required AgentsRuntime Runtime { get; init; }

    public required AgentUsageSnapshot Snapshot { get; init; }

    public required DateTime Now { get; init; }

    public required List<AgentProvider> Providers { get; init; }

    public required AgentPeriod Period { get; init; }

    public required bool UsedDisplay { get; init; }

    public required List<CodexProfileState> CodexAccounts { get; init; }

    public Action? OpenAgentSettings { get; init; }
}

/// <summary>The eight cards of the AI page (spec 03 A7.1).</summary>
internal static class AgentCardViews
{
    private static readonly Color Orange = AgentColors.Orange;

    // ------------------------------------------------------------------------------------------ chrome

    /// <summary>A card on the AI page: radius 18, white 6.5%, padding 12 x 10, content top-leading.</summary>
    public static Border Chrome(UIElement content) => new()
    {
        Child = content,
        Background = AgentColors.White(0.065),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(12, 10, 12, 10),
        SnapsToDevicePixels = true,
        ClipToBounds = true,
    };

    public static FrameworkElement Header(string title, string glyph, Color? tint = null, AgentProvider? provider = null, UIElement? accessory = null, FrameworkElement? customIcon = null)
    {
        var grid = new Grid { Height = 17 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (customIcon is not null)
        {
            // A custom mark standing in for a provider's mark takes the same 17 by 17 slot, so the title does not move with the setting.
            // It is centred in the slot rather than stretched, so it lines up with the title.
            customIcon.HorizontalAlignment = HorizontalAlignment.Center;
            customIcon.VerticalAlignment = VerticalAlignment.Center;
            icon = provider is null ? new Grid { Width = 13, Children = { customIcon } } : new Grid { Width = 17, Height = 17, Children = { customIcon } };
        }
        else if (provider is { } p)
        {
            // A mark is finer than a symbol and needs more room to read.
            icon = new Grid { Width = 17, Height = 17, Children = { AgentMarks.Create(p, 13 * AgentMarks.Scale(p), tint is { } t ? AgentColors.Of(t) : p.TintBrush()) } };
        }
        else
        {
            icon = new Grid { Width = 13, Children = { AU.Icon(glyph, 10, tint is { } t ? AgentColors.Of(t) : AgentColors.Secondary) } };
        }

        grid.Children.Add(icon);
        var text = AU.Text(title, 10.5, FontWeights.SemiBold, AgentColors.Strong, TextTrimming.CharacterEllipsis);
        text.Margin = new Thickness(6, 0, 4, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (accessory is not null)
        {
            Grid.SetColumn(accessory, 2);
            grid.Children.Add(accessory);
        }

        return grid;
    }

    private static StackPanel Body(double spacing = 6) => new() { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };

    private static void Add(StackPanel panel, UIElement child, double top)
    {
        if (child is FrameworkElement fe && panel.Children.Count > 0)
        {
            fe.Margin = new Thickness(fe.Margin.Left, top, fe.Margin.Right, fe.Margin.Bottom);
        }

        panel.Children.Add(child);
    }

    private static string Relative(AgentCardContext c, DateTime date) => AgentFormat.Relative(date, c.Now);

    // ------------------------------------------------------------------------------------------ limits

    /// <summary>Everything a limits card needs to know about whose limits it shows.</summary>
    private sealed record LimitsSource(AgentLimits? Limits, AgentPlan? Plan, string? AccountName, bool IsAccountCard, bool Checking, string? Failure, Action? Refresh);

    private static LimitsSource Resolve(AgentCardContext c, AgentTile tile)
    {
        var provider = tile.Provider!.Value;
        var snapshot = c.Snapshot;
        snapshot.Plans.TryGetValue(provider, out var plan);
        if (provider == AgentProvider.Claude && tile.AccountId is not null)
        {
            // An explicitly chosen Claude profile: only its own checks.
            var status = c.Runtime.Service.Status;
            return new LimitsSource(snapshot.Limits.GetValueOrDefault(AgentProvider.Claude), plan, ClaudeName(status), true, status.ClaudeAccountChecking,
                status.ClaudeAccountFailure is { } f ? AgentStrings.ClaudeCode(f) : null, c.Runtime.Service.CheckClaudeAccount);
        }

        if (tile.AccountId is not null)
        {
            var account = c.CodexAccounts.FirstOrDefault(a => a.Id == tile.AccountId);
            if (account is null)
            {
                return new LimitsSource(null, null, null, true, false, null, null);
            }

            var id = account.Id;
            return new LimitsSource(account.CurrentLimits(c.Now), account.Plan is { Length: > 0 } name ? new AgentPlan(name, null) : null, account.Profile.Name, true, account.Checking,
                account.Failure is { } e ? CodexAccountMessage(e) : null, () => c.Runtime.Accounts.Refresh(id, true));
        }

        var accountName = provider != AgentProvider.Claude ? null
            : c.Runtime.Settings.IncludesClaudeCode() ? ClaudeName(c.Runtime.Service.Status, true)
            : ClaudeAppName(c.Runtime.Service.Status);
        return new LimitsSource(snapshot.Limits.GetValueOrDefault(provider), plan, accountName, false, false, null, null);
    }

    public static FrameworkElement Limits(AgentCardContext c, AgentTile tile)
    {
        var provider = tile.Provider!.Value;
        var snapshot = c.Snapshot;
        var now = c.Now;
        var owner = Resolve(c, tile);
        if (tile.AccountId is not null && owner.Limits is null && owner.AccountName is null && provider != AgentProvider.Claude)
        {
            return Chrome(new Grid());
        }

        var limits = owner.Limits;
        var accountName = owner.AccountName;
        var plan = owner.Plan?.Name;
        var checking = owner.Checking;
        var failure = owner.Failure;
        var refresh = owner.Refresh;
        var isAccountCard = owner.IsAccountCard;
        var stale = AgentSettings.LimitsAreStale(limits, provider, isAccountCard, now);
        var windows = LimitPresenter.Rows(provider, limits, now, c.UsedDisplay, provider == AgentProvider.Claude ? owner.Plan : null);

        var accessory = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (plan is not null)
        {
            var chip = AU.Chip(plan, provider.Tint());
            var describe = AgentLimitPlan.Describe(provider, owner.Plan);
            if (limits?.SessionReported == false && AgentLimitPlan.ExpectsSession(provider, owner.Plan))
            {
                describe += " " + Loc.T("The Claude app reports no 5-hour limit for this account right now.");
            }

            AU.Tip(chip, describe);
            accessory.Children.Add(chip);
        }

        if (isAccountCard)
        {
            // A check waiting its turn shows as one under way.
            if (checking)
            {
                var spinner = new AgentSpinner(12) { Margin = new Thickness(4, 0, 0, 0) };
                accessory.Children.Add(spinner);
            }
            else if (refresh is not null)
            {
                var button = new Button
                {
                    Content = AU.Icon(Glyphs.Refresh, 10, AgentColors.Secondary),
                    Style = AgentButton.PlainStyle,
                    Margin = new Thickness(4, 0, 0, 0),
                    Cursor = Cursors.Hand,
                };
                AU.Tip(button, AgentStrings.CheckLimits);
                button.Click += (_, _) => refresh();
                accessory.Children.Add(button);
            }
        }
        else if (snapshot.Working(provider).Any())
        {
            accessory.Children.Add(new AgentPulse(provider.Tint(), 5) { Margin = new Thickness(2, 0, 0, 0) });
        }

        // The header is only the agent and its plan; the account (an email can be long) is its own quieter line, never cut to fit beside the plan.
        // The mascot stands in for Claude's mark when the setting is on; it fills the same slot as the classic mark and is centred in it.
        FrameworkElement? mascot = provider == AgentProvider.Claude && c.Runtime.Settings.ClaudeMascot()
            ? AgentSymbol.Create(provider, 9, snapshot.Working(provider).Any() ? MascotMood.Working : MascotMood.Idle, true)
            : null;
        var header = Header(provider.DisplayName(), "", provider.Tint(), provider, accessory, mascot);
        var body = Body();
        body.Children.Add(header);
        if (accountName is not null)
        {
            var who = AU.Text(accountName, 9, null, AgentColors.Tertiary, TextTrimming.CharacterEllipsis);
            who.Margin = new Thickness(23, 1, 0, 0);
            var elsewhere = provider == AgentProvider.Claude && tile.AccountId is null && c.Runtime.Service.Status.ClaudeCodeOtherAccount;
            var detail = provider.DisplayName() + " · " + accountName;
            if (provider == AgentProvider.Claude && c.Runtime.Service.Status.ClaudeAccountIdentity is { } identity)
            {
                var lines = new List<string>();
                if (identity.Person is not null)
                {
                    lines.Add(Loc.T("Name") + ": " + identity.Person);
                }

                if (identity.Email is not null)
                {
                    lines.Add(Loc.T("Email") + ": " + identity.Email);
                }

                if (owner.Plan is not null)
                {
                    lines.Add(Loc.T("Plan") + ": " + owner.Plan.Name);
                }

                if (lines.Count > 0)
                {
                    detail = string.Join(Environment.NewLine, lines);
                }
            }

            AU.Tip(who, detail + (elsewhere ? Environment.NewLine + Loc.T("Claude Code in your terminal is signed in to another account. This card follows the Claude app.") : ""));
            body.Children.Add(who);
        }

        if (isAccountCard)
        {
            var notes = AgentSettings.AccountNotes(windows.Count > 0, checking, failure, AgentStrings.RefreshHint, AgentStrings.Loading,
                limits is null ? null : AgentStrings.Updated(Relative(c, limits.ObservedAt)));
            if (notes.Placeholder is { } placeholder)
            {
                var t = AU.Text(placeholder, 10, null, AgentColors.Secondary);
                t.TextWrapping = TextWrapping.Wrap;
                t.MaxHeight = 28;
                Add(body, t, 6);
            }

            if (windows.Count > 0)
            {
                Add(body, Meters(windows, stale), 6);
            }

            if (notes.Caption is { } note)
            {
                Add(body, AU.Text(note, 8.5, null, failure is null && !stale ? AgentColors.Secondary : AgentColors.Of(Orange), TextTrimming.CharacterEllipsis), 6);
            }
        }
        else if (windows.Count == 0)
        {
            Estimate(c, provider, body);
        }
        else
        {
            Add(body, Meters(windows, stale), 6);
            if (limits is not null)
            {
                // Where the numbers come from and how old they are: every reading says so, and an old one is not dressed up as fresh.
                var (source, old) = LimitPresenter.SourceLine(limits, now);
                var line = AU.Text(source + (old ? " · " + Loc.T("may be out of date") : ""), 9, null, old ? AgentColors.Of(Orange) : AgentColors.Tertiary, TextTrimming.CharacterEllipsis);
                AU.Tip(line, limits.Origin == LimitOrigins.ClaudeDesktop
                    ? Loc.T("The Claude app saves your plan usage about every 15 minutes while it runs with its icon in the notification area. The reset time is worked out from when the weekly figure last dropped.")
                    : Loc.T("Open the AI Agents settings, \"Where these numbers come from\", to compare every source."));
                Add(body, line, 5);
                if (LimitPresenter.Breakdown(limits) is { } breakdown)
                {
                    var uses = AU.Text(LimitPresenter.BreakdownPrefix + breakdown, 9, null, AgentColors.Secondary);
                    uses.TextWrapping = TextWrapping.Wrap;
                    AU.Tip(uses, "Share of this week's plan use by product, as Claude reports it" + (limits.BreakdownAsOf is { } at ? " (as of " + AgentFormat.Relative(at, now, false) + ")" : "") + ".");
                    Add(body, uses, 3);
                }

                if (LimitPresenter.Disagreement(snapshot, provider, now) is { } diff)
                {
                    var other = AU.Text(diff, 8.5, null, AgentColors.Tertiary);
                    other.TextWrapping = TextWrapping.Wrap;
                    Add(body, other, 3);
                }
            }
        }

        return Chrome(body);
    }

    /// <summary>The height a limits card needs for what it shows (the page sizes its row from this).</summary>
    public static double LimitsHeight(AgentCardContext c, AgentTile tile)
    {
        var provider = tile.Provider!.Value;
        var source = Resolve(c, tile);
        var floor = tile.AccountId is not null ? AgentSettings.AccountCardHeight : AgentSettings.CardHeight;
        var rows = LimitPresenter.Rows(provider, source.Limits, c.Now, c.UsedDisplay, provider == AgentProvider.Claude ? source.Plan : null);
        if (rows.Count == 0)
        {
            return floor + (source.AccountName is not null && tile.AccountId is null ? 14 : 0);
        }

        var breakdown = source.IsAccountCard ? null : LimitPresenter.Breakdown(source.Limits);
        var disagreement = source.IsAccountCard ? null : LimitPresenter.Disagreement(c.Snapshot, provider, c.Now);
        return Math.Max(floor, LimitPresenter.CardHeight(rows, breakdown, disagreement) + (source.AccountName is not null ? 14 : 0));
    }

    private static string CodexAccountMessage(CodexAccountError e) => e switch
    {
        CodexAccountError.MissingCli => AgentStrings.ResetsNeedCodex,
        CodexAccountError.InvalidProfile => AgentStrings.FolderProblem,
        CodexAccountError.NeedsSignIn => AgentStrings.CodexSignIn,
        CodexAccountError.Outdated => AgentStrings.CodexUpdate,
        _ => AgentStrings.CodexFailed,
    };

    /// <summary>With Claude Code left out, "Claude" is the Claude app: the line says so, with the email the plan belongs to when it is known.</summary>
    public static string ClaudeAppName(AgentServiceStatus status)
    {
        var identity = status.ClaudeAccountIdentity;
        // With the person's name the line names the account; the tooltip still says it is the Claude app.
        var parts = new List<string>();
        if (identity?.Person is { } person)
        {
            parts.Add(person);
        }
        else
        {
            parts.Add(Loc.T("Claude app"));
        }

        if (identity?.Email is { } email)
        {
            parts.Add(email);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The Claude account's name: the profile's name and, when known, its email; the default account shows just the email.</summary>
    public static string ClaudeName(AgentServiceStatus status, bool cardOnly = false)
    {
        var name = status.ClaudeProfile.Name(AgentStrings.DefaultAccount);
        var person = status.ClaudeAccountIdentity?.Person;
        var email = status.ClaudeAccountIdentity?.Email;
        if (person is null && email is null)
        {
            return name;
        }

        // A chosen profile is named first; the default one is just the person and their email.
        var parts = new List<string>();
        if (!status.ClaudeProfile.IsDefault)
        {
            parts.Add(name);
        }

        if (person is not null)
        {
            parts.Add(person);
        }

        if (email is not null)
        {
            parts.Add(email);
        }

        return string.Join(" · ", parts);
    }

    private static FrameworkElement Meters(List<LimitRowModel> rows, bool stale)
    {
        var panel = new StackPanel { Opacity = stale ? 0.6 : 1 };
        foreach (var row in rows)
        {
            Add(panel, LimitRow(row), 5);
        }

        return panel;
    }

    private static readonly Color AlertRed = Color.FromRgb(0xFF, 0x45, 0x3A);

    /// <summary>A row says the window's name and how much is used or left, with a bar; a note says why it is paused, used up or not started yet.</summary>
    private static FrameworkElement LimitRow(LimitRowModel model)
    {
        var row = new StackPanel();
        var line = new AgentFitRow { Height = 15 };
        line.Children.Add(AU.Text(model.Label, 10, FontWeights.Medium, AgentColors.White(0.85), TextTrimming.CharacterEllipsis));
        var quiet = model.Fraction is null && !model.NoteIsAlert;
        var share = AU.Text(model.Value, model.Fraction is null ? 11 : 12, FontWeights.SemiBold, quiet || model.Dimmed ? AgentColors.Secondary : AgentColors.Of(model.Tint, 1), display: true, mono: true);
        line.Children.Add(share);
        row.Children.Add(line);
        if (model.Fraction is { } fraction)
        {
            var meter = new AgentMeter(fraction, model.Pace, model.Dimmed ? Color.FromRgb(0x8E, 0x8E, 0x93) : model.Tint, 4, model.Dimmed) { Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(meter);
        }

        if (model.Note is { } note)
        {
            var t = AU.Text(note, 9.5, model.NoteIsAlert ? FontWeights.Medium : null, model.NoteIsAlert ? AgentColors.Of(AlertRed) : AgentColors.Secondary);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 2, 0, 0);
            row.Children.Add(t);
        }

        if (model.Reset is not null)
        {
            var reset = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            reset.Children.Add(AU.Icon(Glyphs.Refresh, 7.5, AgentColors.Secondary));
            var text = AU.Text(Loc.T("resets") + " " + model.Reset, 9.5, null, AgentColors.Secondary, TextTrimming.CharacterEllipsis);
            text.Margin = new Thickness(3, 0, 0, 0);
            reset.Children.Add(text);
            row.Children.Add(reset);
        }

        AU.Tip(row, model.Tip);
        return row;
    }

    /// <summary>How long until the window renews: days and hours, or hours and minutes, never seconds on a card that redraws every few.</summary>
    private static string Countdown(DateTime date, DateTime now)
    {
        var seconds = (date - now).TotalSeconds;
        return AgentFormat.Duration(Math.Max(60, seconds), seconds >= 3600 ? 2 : 1);
    }

    /// <summary>Without a recent reading from the Claude app, the session still ends five hours after its first request.</summary>
    private static void Estimate(AgentCardContext c, AgentProvider provider, StackPanel body)
    {
        var snapshot = c.Snapshot;
        var now = c.Now;
        snapshot.Plans.TryGetValue(provider, out var plan);
        if (provider == AgentProvider.Claude && snapshot.ClaudeBlock is { } block && AgentLimitPlan.HasSessionWindow(provider, plan))
        {
            var length = (block.End - block.Start).TotalSeconds;
            var box = new StackPanel();
            var line = new Grid { Height = 15 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(AU.Text(Loc.T("5-hour session"), 10, FontWeights.Medium, AgentColors.White(0.85)));
            var countdown = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 0, 0) };
            countdown.Children.Add(AU.Icon(Glyphs.Refresh, 7.5, AgentColors.Secondary));
            var ct = AU.Text(Countdown(block.End, now), 9.5, null, AgentColors.Secondary);
            ct.Margin = new Thickness(2, 0, 0, 0);
            countdown.Children.Add(ct);
            left.Children.Add(countdown);
            line.Children.Add(left);
            var cost = AU.Text(AgentFormat.Cost(block.Totals.Cost), 12, FontWeights.SemiBold, AgentColors.Primary, display: true, mono: true);
            Grid.SetColumn(cost, 2);
            line.Children.Add(cost);
            box.Children.Add(line);
            var meter = new AgentMeter(length > 0 ? (now - block.Start).TotalSeconds / length : 0, null, provider.Tint(), 4, true) { Margin = new Thickness(0, 2, 0, 0) };
            box.Children.Add(meter);
            AU.Tip(box, AgentStrings.Estimated);
            Add(body, box, 6);
            Add(body, SetUpLimits(c, provider), 6);
        }
        else if (provider == AgentProvider.Claude)
        {
            Add(body, AU.Text(AgentStrings.NoSession, 10.5, null, AgentColors.Secondary), 6);
            Add(body, SetUpLimits(c, provider), 6);
        }
        else if (provider == AgentProvider.OpenCode)
        {
            var today = snapshot.Usage(AgentPeriod.Today).ByProvider.GetValueOrDefault(AgentProvider.OpenCode);
            if (today.Tokens.Total > 0 || today.Requests > 0 || today.Cost > 0)
            {
                var costText = today.Unpriced > 0 ? (today.Cost > 0 ? "≥ " + AgentFormat.Cost(today.Cost) : "—") : AgentFormat.Cost(today.Cost);
                var box = new StackPanel();
                var line = new Grid { Height = 15 };
                line.Children.Add(AU.Text(AgentStrings.Today, 10, FontWeights.Medium, AgentColors.White(0.85)));
                var cost = AU.Text(costText, 12, FontWeights.SemiBold, AgentColors.Primary, display: true, mono: true);
                cost.HorizontalAlignment = HorizontalAlignment.Right;
                line.Children.Add(cost);
                AU.Tip(line, today.Unpriced > 0 ? AgentStrings.Unpriced : AgentStrings.ValueNote);
                box.Children.Add(line);
                var foot = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                left.Children.Add(AU.Text(AgentFormat.Tokens(today.Tokens.Total), 9.5, null, AgentColors.Secondary));
                if (today.Tokens.CacheHitRate is { } rate and > 0)
                {
                    left.Children.Add(AU.Text(" · " + AgentStrings.Cached(AgentFormat.Percent(rate)), 9.5, null, AgentColors.Tertiary));
                }

                foot.Children.Add(left);
                if (LastUsed(c, provider) is { } last)
                {
                    last.HorizontalAlignment = HorizontalAlignment.Right;
                    foot.Children.Add(last);
                }

                box.Children.Add(foot);
                Add(body, box, 6);
            }
            else
            {
                Add(body, AU.Text(AgentStrings.NoSession, 10.5, null, AgentColors.Secondary), 6);
                if (LastUsed(c, provider) is { } last)
                {
                    Add(body, last, 4);
                }
            }
        }
        else
        {
            var t = AU.Text(AgentStrings.WaitingForLimits, 10.5, null, AgentColors.Secondary);
            t.TextWrapping = TextWrapping.Wrap;
            Add(body, t, 6);
            if (LastUsed(c, provider) is { } last)
            {
                Add(body, last, 4);
            }
        }
    }

    private static FrameworkElement? LastUsed(AgentCardContext c, AgentProvider provider) =>
        c.Snapshot.LastActivity.TryGetValue(provider, out var last) ? AU.Text(Relative(c, last), 9.5, null, AgentColors.Tertiary) : null;

    /// <summary>Claude's plan limits come from Claude Code or the Claude app; the settings say how, and why Claude Code could not check.</summary>
    private static FrameworkElement SetUpLimits(AgentCardContext c, AgentProvider provider)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(AU.Text(AgentStrings.ClaudeLimitsHint, 9.5, FontWeights.Medium, provider.TintBrush(), TextTrimming.CharacterEllipsis));
        var arrow = AU.Icon("", 8, provider.TintBrush());
        arrow.Margin = new Thickness(2, 0, 0, 0);
        content.Children.Add(arrow);
        var button = new Button { Content = content, Style = AgentButton.PlainStyle, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        button.Click += (_, _) => c.OpenAgentSettings?.Invoke();
        return button;
    }

    // ------------------------------------------------------------------------------------------ spending

    public static FrameworkElement Spend(AgentCardContext c)
    {
        var usage = c.Snapshot.Usage(c.Period);
        var accessory = new StackPanel { Orientation = Orientation.Horizontal };
        if (c.Period == AgentPeriod.Month)
        {
            foreach (var provider in c.Providers)
            {
                if (c.Snapshot.Plans.TryGetValue(provider, out var plan) && plan.MonthlyPrice is > 0 and var price
                    && usage.ByProvider.TryGetValue(provider, out var totals) && totals.Cost > 0)
                {
                    var ratio = totals.Cost / price;
                    var multiple = ratio.ToString(ratio < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + "×";
                    var chip = AU.Chip(multiple, provider.Tint());
                    chip.Margin = new Thickness(3, 0, 0, 0);
                    AU.Tip(chip, AgentStrings.PlanMultiple(multiple, provider.DisplayName() + " " + plan.Name));
                    accessory.Children.Add(chip);
                }
            }
        }

        var body = Body();
        body.Children.Add(Header(AgentStrings.SpendCard, AgentCard.Spend.Glyph(), null, null, accessory, AgentMarks.DollarCircle(11, AgentColors.Secondary)));
        var value = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        value.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        value.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var amount = AU.Text((usage.FullyPriced ? "" : "≥ ") + AgentFormat.Cost(usage.Total.Cost), 22, FontWeights.Medium, AgentColors.Primary, TextTrimming.None, true, true);
        // Dollar figures here are what the same tokens would cost at API list prices. A subscription charges a fixed price instead.
        var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 2) };
        caption.Children.Add(AU.Text(AgentStrings.ApiEquivalent, 9.5, FontWeights.Medium, AgentColors.Strong, TextTrimming.CharacterEllipsis));
        caption.Children.Add(AU.Text(AgentStrings.NotWhatYouPay, 9, null, AgentColors.Secondary, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(caption, 1);
        value.Children.Add(amount);
        value.Children.Add(caption);
        AU.Tip(value, usage.FullyPriced ? AgentStrings.ValueNote : AgentStrings.Unpriced);
        body.Children.Add(value);
        var split = new AgentProviderSplit(c.Providers, usage) { Margin = new Thickness(0, 5, 0, 0) };
        body.Children.Add(split);
        var period = AU.Text(AgentStrings.PeriodRange(c.Period), 9.5, FontWeights.Medium, AgentColors.Secondary, TextTrimming.CharacterEllipsis);
        period.Margin = new Thickness(0, 4, 0, 0);
        AU.Tip(period, AgentStrings.PeriodHelp(c.Period));
        body.Children.Insert(2, period);
        var t = usage.Total.Tokens;
        var tokens = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        tokens.Children.Add(AU.Text(AgentStrings.Tokens(AgentFormat.Tokens(t.Total)) + " · in " + AgentFormat.Tokens(t.Input) + " · out " + AgentFormat.Tokens(t.Output), 9.5, null, AgentColors.Secondary, TextTrimming.CharacterEllipsis, false, true));
        tokens.Children.Add(AU.Text("cache read " + AgentFormat.Tokens(t.CacheRead) + " · cache write " + AgentFormat.Tokens(t.CacheWrite), 9.5, null, AgentColors.Tertiary, TextTrimming.CharacterEllipsis, false, true));
        AU.Tip(tokens, AgentStrings.Saved(AgentFormat.Cost(usage.Total.Savings)));
        body.Children.Add(tokens);
        return Chrome(body);
    }

    // ------------------------------------------------------------------------------------------ now

    public static FrameworkElement Live(AgentCardContext c) => AgentSessionCard.Build(c);

    // ------------------------------------------------------------------------------------------ trend

    public static FrameworkElement Trend(AgentCardContext c)
    {
        var snapshot = c.Snapshot;
        var period = c.Period;
        var buckets = period == AgentPeriod.Today ? snapshot.Hours : snapshot.Days.TakeLast(period.Days()).ToList();
        var byCost = snapshot.Usage(period).FullyPriced;
        var caption = AU.Text("", 9.5, FontWeights.Medium, AgentColors.Secondary, TextTrimming.CharacterEllipsis, false, true);
        string Default() => AgentStrings.Period(period) + " · " + Value(snapshot.Usage(period).Total, byCost);
        caption.Text = Default();
        var body = Body();
        body.Children.Add(Header(AgentStrings.TrendCard, AgentCard.Trend.Glyph(), null, null, caption));
        var bars = new AgentBars(buckets, c.Providers, byCost) { Height = 52, Margin = new Thickness(0, 6, 0, 0), VerticalAlignment = VerticalAlignment.Stretch };
        bars.HoverChanged += bucket =>
        {
            if (bucket is null)
            {
                caption.Text = Default();
                return;
            }

            // A pointed-at bar reads its tokens beside its cost.
            var tokens = byCost ? " · " + Value(bucket.Total, false) : "";
            caption.Text = Label(period, bucket.Start, true) + " · " + Value(bucket.Total, byCost) + tokens;
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        var header = body.Children[0];
        body.Children.Clear();
        grid.Children.Add(header);
        bars.Margin = new Thickness(0, 6, 0, 3);
        bars.Height = double.NaN;
        Grid.SetRow(bars, 1);
        grid.Children.Add(bars);
        var axis = Axis(period, buckets);
        Grid.SetRow(axis, 2);
        grid.Children.Add(axis);
        return Chrome(grid);
    }

    private static string Value(AgentTotals totals, bool byCost) => byCost ? AgentFormat.Cost(totals.Cost) : AgentStrings.Tokens(AgentFormat.Tokens(totals.Tokens.Total));

    private static string Label(AgentPeriod period, DateTime date, bool full)
    {
        var culture = CultureInfo.CurrentCulture;
        if (period == AgentPeriod.Today)
        {
            return date.ToString(culture.DateTimeFormat.ShortTimePattern.Contains('t') ? "h tt" : "HH", culture);
        }

        if (period == AgentPeriod.Week && !full)
        {
            return culture.DateTimeFormat.GetDayName(date.DayOfWeek)[..1].ToUpper(culture);
        }

        return date.ToString("d MMM", culture);
    }

    /// <summary>Every bar a letter over a week; the ends and the middle otherwise.</summary>
    private static FrameworkElement Axis(AgentPeriod period, List<AgentBucket> buckets)
    {
        var grid = new Grid { Height = 10 };
        if (buckets.Count == 0)
        {
            return grid;
        }

        TextBlock Mark(int index)
        {
            var t = AU.Text(Label(period, buckets[index].Start, false), 8.5, FontWeights.Medium, AgentColors.Tertiary);
            return t;
        }

        if (period == AgentPeriod.Week)
        {
            for (var i = 0; i < buckets.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var t = Mark(i);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(t, i);
                grid.Children.Add(t);
            }
        }
        else
        {
            var first = Mark(0);
            first.HorizontalAlignment = HorizontalAlignment.Left;
            var middle = Mark(buckets.Count / 2);
            middle.HorizontalAlignment = HorizontalAlignment.Center;
            var last = Mark(buckets.Count - 1);
            last.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(first);
            grid.Children.Add(middle);
            grid.Children.Add(last);
        }

        return grid;
    }

    // ------------------------------------------------------------------------------------------ models / projects

    public static FrameworkElement Shares(AgentCardContext c, string title, string glyph, List<AgentShare> shares, bool byCost)
    {
        var shown = shares.Take(3).ToList();
        var peak = Math.Max(shown.Select(s => s.Totals.Weight(byCost)).DefaultIfEmpty(0).Max(), double.Epsilon);
        var body = Body();
        body.Children.Add(Header(title, glyph));
        if (shown.Count == 0)
        {
            Add(body, AU.Text(AgentStrings.NoActivity, 10.5, null, AgentColors.Secondary), 6);
        }
        else
        {
            var rows = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var share in shown)
            {
                Add(rows, ShareRow(share, peak, byCost), 5);
            }

            body.Children.Add(rows);
        }

        return Chrome(body);
    }

    private static FrameworkElement ShareRow(AgentShare share, double peak, bool byCost)
    {
        var weight = share.Totals.Weight(byCost);
        var tint = share.Provider?.Tint() ?? Colors.White;
        var row = new Grid { Height = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        row.Children.Add(AU.Text(share.Name, 10.5, FontWeights.Medium, AgentColors.Primary, TextTrimming.CharacterEllipsis));
        var bar = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Max(3, 44 * weight / peak),
            Height = 4,
            RadiusX = 2,
            RadiusY = 2,
            Fill = AgentColors.Of(tint, 0.85),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var value = AU.Text(byCost ? AgentFormat.Cost(share.Totals.Cost) : AgentFormat.Tokens(share.Totals.Tokens.Total), 10, FontWeights.Medium, AgentColors.Secondary, mono: true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        AU.Tip(row, string.Join(" · ", share.Name, AgentStrings.Tokens(AgentFormat.Tokens(share.Totals.Tokens.Total)), AgentFormat.Cost(share.Totals.Cost)));
        return row;
    }

    // ------------------------------------------------------------------------------------------ activity

    public static FrameworkElement Activity(AgentCardContext c)
    {
        var days = c.Snapshot.Days;
        var byCost = days.All(d => d.Total.Unpriced == 0);
        // Days in a row with any use, counting today only once it has some.
        var streak = 0;
        for (var i = days.Count - 1; i >= 0; i--)
        {
            if (days[i].Total.Requests > 0)
            {
                streak++;
            }
            else if (streak > 0 || i != days.Count - 1)
            {
                break;
            }
        }

        UIElement? accessory = null;
        if (streak > 1)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            s.Children.Add(AU.Icon("", 9, AgentColors.Of(Orange)));
            var n = AU.Text(streak.ToString(CultureInfo.CurrentCulture), 9.5, FontWeights.SemiBold, AgentColors.Of(Orange));
            n.Margin = new Thickness(2, 0, 0, 0);
            s.Children.Add(n);
            AU.Tip(s, AgentStrings.Streak);
            accessory = s;
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Header(AgentStrings.ActivityCard, AgentCard.Activity.Glyph(), null, null, accessory));
        var inner = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(inner, 1);
        grid.Children.Add(inner);
        inner.SizeChanged += (_, e) =>
        {
            inner.Children.Clear();
            inner.ColumnDefinitions.Clear();
            var map = AgentHeatmap.WidthFor(e.NewSize.Height, days);
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(map) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var heat = new AgentHeatmap(days, byCost);
            inner.Children.Add(heat);
            var figures = new Grid();
            Grid.SetColumn(figures, 2);
            inner.Children.Add(figures);
            BuildFigures(figures, c, days, byCost, null);
            heat.HoverChanged += day => BuildFigures(figures, c, days, byCost, day);
        };
        return Chrome(grid);
    }

    /// <summary>The total for the whole map, its active days and its busiest day; a hovered day takes the place of the busiest one.</summary>
    private static void BuildFigures(Grid figures, AgentCardContext c, List<AgentBucket> days, bool byCost, AgentBucket? pointed)
    {
        figures.Children.Clear();
        figures.RowDefinitions.Clear();
        var active = days.Where(d => d.Total.Requests > 0).ToList();
        var busiest = active.OrderByDescending(d => d.Total.Weight(byCost)).FirstOrDefault();
        var all = new AgentTotals();
        foreach (var d in days)
        {
            all.Add(d.Total);
        }

        var stack = new StackPanel();
        stack.Children.Add(AU.Text(FigureValue(all, byCost), 17, FontWeights.Medium, AgentColors.Primary, TextTrimming.None, true, true));
        stack.Children.Add(AU.Text(AgentFormat.Span(days.Count), 9.5, null, AgentColors.Secondary));
        figures.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        figures.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        figures.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        figures.Children.Add(stack);
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(Figure(AgentStrings.ActiveDays, active.Count.ToString(CultureInfo.CurrentCulture)));
        var day = pointed ?? busiest;
        if (day is not null)
        {
            var label = pointed is null ? AgentStrings.BusiestDay : CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day.Start.DayOfWeek);
            var fig = Figure(label, day.Start.ToString("d MMM", CultureInfo.CurrentCulture) + " · " + FigureValue(day.Total, byCost));
            fig.Margin = new Thickness(0, 2, 0, 0);
            bottom.Children.Add(fig);
        }

        Grid.SetRow(bottom, 2);
        figures.Children.Add(bottom);
    }

    private static FrameworkElement Figure(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(AU.Text(label, 10, FontWeights.Medium, AgentColors.Secondary, TextTrimming.CharacterEllipsis));
        var v = AU.Text(value, 10, FontWeights.Medium, AgentColors.Primary, TextTrimming.CharacterEllipsis, false, true);
        v.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private static string FigureValue(AgentTotals totals, bool byCost) => byCost ? AgentFormat.Cost(totals.Cost) : AgentFormat.Tokens(totals.Tokens.Total);

    // ------------------------------------------------------------------------------------------ resets

    /// <summary>
    /// Codex's banked resets: how many the account holds, when the next one expires, and a use that asks first. Cancel takes the place
    /// of the button that asked, so a double click never spends one.
    /// </summary>
    public static FrameworkElement Resets(AgentCardContext c, AgentTile tile, Action rebuild)
    {
        var resets = c.Runtime.Resets;
        var tint = AgentProvider.Codex.Tint();
        var summary = resets.Summary;
        UIElement? accessory = null;
        if (summary is { Available: > 0 })
        {
            accessory = AU.Chip(summary.Available.ToString(CultureInfo.CurrentCulture), tint);
        }
        else if (resets.Checking)
        {
            accessory = new AgentSpinner(12);
        }

        var body = Body();
        body.Children.Add(Header(AgentStrings.ResetsCard, AgentCard.Resets.Glyph(), tint, AgentProvider.Codex, accessory));
        if (tile.NamesDefaultAccount)
        {
            Add(body, AU.Text("Codex · " + AgentStrings.DefaultAccount, 8.5, null, AgentColors.Secondary, TextTrimming.CharacterEllipsis), 3);
        }

        var finished = resets.LastFinished is { } f && (c.Now - f.Date).TotalSeconds < 60 ? f : null;
        var content = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Put(UIElement? message, UIElement? buttons)
        {
            if (message is not null)
            {
                Grid.SetRow(message, 0);
                content.Children.Add(message);
            }

            if (buttons is not null)
            {
                Grid.SetRow(buttons, 2);
                content.Children.Add(buttons);
            }
        }

        FrameworkElement Line(string message, string? glyph = null, Color? color = null, bool secondary = true)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph is not null)
            {
                var icon = AU.Icon(glyph, 10, AgentColors.Of(color ?? Colors.White));
                icon.Margin = new Thickness(0, 0, 4, 0);
                row.Children.Add(icon);
            }

            var text = AU.Text(message, 10.5, FontWeights.Medium, glyph is null && secondary ? AgentColors.Secondary : AgentColors.Strong);
            text.TextWrapping = TextWrapping.Wrap;
            text.MaxWidth = 200;
            row.Children.Add(text);
            return row;
        }

        FrameworkElement Pill(string title, bool prominent, Action click, string? glyph = null)
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            var brush = prominent ? AgentColors.Of(tint) : AgentColors.White(0.85);
            if (glyph is not null)
            {
                var icon = AU.Icon(glyph, 8.5, brush);
                icon.Margin = new Thickness(0, 0, 3, 0);
                inner.Children.Add(icon);
            }

            inner.Children.Add(AU.Text(title, 10.5, FontWeights.SemiBold, brush, TextTrimming.CharacterEllipsis));
            var button = new Button
            {
                Content = new Border { Child = inner, Height = 20, Padding = new Thickness(9, 0, 9, 0), CornerRadius = new CornerRadius(10), Background = prominent ? AgentColors.Of(tint, 0.2) : AgentColors.White(0.1) },
                Style = AgentButton.PlainStyle,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => click();
            return button;
        }

        var confirming = c.Runtime.ResetConfirming;
        if (resets.Redeeming)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new AgentSpinner(12) { Margin = new Thickness(0, 0, 5, 0) });
            row.Children.Add(Line(AgentStrings.Resetting));
            Put(row, null);
        }
        else if (confirming && summary is { Available: > 0 })
        {
            var message = AU.Text(AgentStrings.ResetConfirm, 10.5, FontWeights.Medium, AgentColors.Strong);
            message.TextWrapping = TextWrapping.Wrap;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Pill(AgentStrings.Cancel, false, () =>
            {
                c.Runtime.ResetConfirming = false;
                rebuild();
            }));
            var confirm = Pill(AgentStrings.ConfirmReset, true, () =>
            {
                c.Runtime.ResetConfirming = false;
                resets.Redeem();
                rebuild();
            });
            confirm.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(confirm);
            Put(message, buttons);
        }
        else if (finished is not null)
        {
            if (finished.Outcome is null && summary is { Available: > 0 })
            {
                // Trying again repeats the same use, which never spends a second reset.
                Put(Outcome(null), Pill(AgentStrings.UseReset, true, () => { c.Runtime.ResetConfirming = true; rebuild(); }, ""));
            }
            else
            {
                Put(Outcome(finished.Outcome), null);
            }
        }
        else if (summary is null && resets.Failure is { } failure)
        {
            var message = AU.Text(FailureMessage(failure), 10.5, null, AgentColors.Secondary);
            message.TextWrapping = TextWrapping.Wrap;
            Put(message, Pill(AgentStrings.Retry, false, () => resets.Refresh(failure == CodexFailure.Missing)));
        }
        else if (summary is not null)
        {
            if (summary.Available > 0)
            {
                // A reset a day from expiring is worth using soon.
                var soon = summary.NextExpiry is { } ne && (ne - c.Now).TotalSeconds < 86_400;
                var message = summary.NextExpiry is { } expiry ? AU.Text(AgentStrings.ResetsExpiry(AgentFormat.Relative(expiry, c.Now, false)), 10.5, null, soon ? AgentColors.Of(Orange) : AgentColors.Secondary) : null;
                if (message is not null)
                {
                    message.TextWrapping = TextWrapping.Wrap;
                }

                Put(message, Pill(AgentStrings.UseReset, true, () => { c.Runtime.ResetConfirming = true; rebuild(); }, ""));
            }
            else
            {
                Put(Line(AgentStrings.ResetsNone), null);
            }
        }

        FrameworkElement Outcome(CodexOutcome? outcome) => outcome switch
        {
            CodexOutcome.Reset => Line(AgentStrings.ResetDone, "", AgentColors.Green),
            CodexOutcome.NothingToReset => Line(AgentStrings.ResetNotNeeded, "", Colors.Gray),
            CodexOutcome.AlreadyRedeemed => Line(AgentStrings.ResetTaken, "", Colors.Gray),
            CodexOutcome.NoCredit => Line(AgentStrings.ResetsNone),
            _ => Line(AgentStrings.ResetFailed, "", Orange),
        };

        body.Children.Add(content);
        var card = Chrome(body);
        AU.Tip(card, AgentStrings.ResetsHelp);
        return card;
    }

    private static string FailureMessage(CodexFailure f) => f switch
    {
        CodexFailure.Missing => AgentStrings.ResetsNeedCodex,
        CodexFailure.NeedsSignIn => AgentStrings.ResetsSignIn,
        CodexFailure.Outdated => AgentStrings.ResetsUpdate,
        _ => AgentStrings.ResetsCheckFailed,
    };
}

/// <summary>The stacked bar that splits a period by agent: every agent with use keeps a 4 px sliver, the rest shares what is left.</summary>
internal sealed class AgentProviderSplit : FrameworkElement
{
    private readonly List<(Color Tint, double Weight)> _parts;

    public AgentProviderSplit(List<AgentProvider> providers, AgentPeriodUsage usage)
    {
        var byCost = usage.FullyPriced;
        _parts = providers.Select(p => (p.Tint(), usage.ByProvider.TryGetValue(p, out var t) ? t.Weight(byCost) : 0)).ToList();
        Height = 4;
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, 4);

    protected override void OnRender(DrawingContext dc)
    {
        var total = _parts.Sum(p => p.Weight);
        if (total <= 0)
        {
            dc.DrawRoundedRectangle(AgentColors.White(0.13), null, new Rect(0, 0, ActualWidth, 4), 2, 2);
            return;
        }

        var shown = _parts.Count(p => p.Weight > 0);
        const double gap = 2;
        var room = Math.Max(0, ActualWidth - gap * Math.Max(0, shown - 1) - 4 * shown);
        var x = 0.0;
        foreach (var (tint, weight) in _parts)
        {
            if (weight <= 0)
            {
                continue;
            }

            var width = 4 + room * weight / total;
            dc.DrawRoundedRectangle(AgentColors.Of(tint, 0.9), null, new Rect(x, 0, width, 4), 2, 2);
            x += width + gap;
        }
    }
}

/// <summary>A tiny indeterminate spinner (the mini progress view of the cards).</summary>
internal sealed class AgentSpinner : Grid
{
    private readonly RotateTransform _rotate = new();
    private IDisposable? _subscription;

    public AgentSpinner(double size)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 6,1.2 A 4.8,4.8 0 1 1 1.2,6"),
            Stroke = AgentColors.White(0.75),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _rotate,
        };
        Children.Add(new Viewbox { Child = new Canvas { Width = 12, Height = 12, Children = { arc } }, Stretch = Stretch.Uniform });
        arc.Width = 12;
        arc.Height = 12;
        // Turns only while it can be seen: a card on a hidden page or a closed island stays loaded, and the clock would keep
        // stepping (and the island repainting) for nobody.
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Sync();
    }

    private void Sync()
    {
        if (IsVisible && IsLoaded)
        {
            _subscription ??= AgentClock.Instance.Subscribe(t => _rotate.Angle = t * 360 % 360);
        }
        else
        {
            Stop();
        }
    }

    private void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}

/// <summary>Button styles for the island's dark controls.</summary>
internal static class AgentButton
{
    /// <summary>No chrome: the content is the button.</summary>
    public static readonly Style PlainStyle = CreatePlain();

    private static Style CreatePlain()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = presenter;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Seal();
        return style;
    }
}
