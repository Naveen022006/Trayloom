// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>An expanded island page. Created when shown, dropped when the island collapses.</summary>
internal abstract class IslandPage
{
    protected IslandPage(IslandCore core, IslandWindow? window)
    {
        Core = core;
        _window = window;
    }

    protected IslandCore Core { get; }

    private readonly IslandWindow? _window;

    protected IslandWindow Window => _window!;

    protected bool HasWindow => _window is not null;

    public abstract string ModuleId { get; }

    public abstract FrameworkElement View { get; }

    /// <summary>Items for the leading side of the top row (null = none).</summary>
    public virtual FrameworkElement? Leading => null;

    /// <summary>Items for the trailing side of the top row.</summary>
    public virtual FrameworkElement? Trailing => null;

    /// <summary>Replaces leading and trailing: one element across the whole top row (the Controls levels).</summary>
    public virtual FrameworkElement? BandFill => null;

    /// <summary>False for pages without a top row (System).</summary>
    public virtual bool HasTopRow => true;

    /// <summary>Natural content width (the island takes min(chosen, natural + 56) under a preset).</summary>
    public virtual double? NaturalWidth => null;

    /// <summary>Desired content height for the given content width, never more than <paramref name="budget"/>.</summary>
    public abstract double Height(double width, double budget);

    public virtual void Shown()
    {
    }

    public virtual void Hidden()
    {
    }

    /// <summary>Esc: close an inner layer; true when something was closed.</summary>
    public virtual bool StepBack() => false;

    /// <summary>The page has a vertically scrolling list (gestures leave the wheel to it).</summary>
    public virtual bool ScrollsContent => false;

    /// <summary>The page may grow beyond the preset content budget (Music with lyrics or the queue open).</summary>
    public virtual bool GrowsBeyondBudget => false;

    /// <summary>The page takes text input (the island window must be able to take keyboard focus).</summary>
    public virtual bool WantsKeyboard => false;

    /// <summary>Key handling while the island has focus (return true when handled).</summary>
    public virtual bool HandleKey(KeyEventArgs e) => false;

    /// <summary>Called when the content size changed so the page can re-layout (e.g. columns).</summary>
    public virtual void Resized(double width, double height)
    {
    }

    /// <summary>The page opened inside the settings preview: no camera, no keyboard, no timers.</summary>
    public bool IsPreview { get; set; }
}

/// <summary>Wraps a section contributed by another project (for example AI Agents).</summary>
internal sealed class ExternalPage : IslandPage
{
    private readonly IIslandSection _section;
    private FrameworkElement? _view;

    public ExternalPage(IslandCore core, IslandWindow? window, IIslandSection section)
        : base(core, window) => _section = section;

    public override string ModuleId => _section.Id;

    public override FrameworkElement View => _view ??= Create();

    public override bool ScrollsContent => true;

    private FrameworkElement Create()
    {
        try
        {
            return _section.CreateView(Core.IslandContext);
        }
        catch (Exception ex)
        {
            Core.Ctx.Log.Warn($"Island page {_section.Id} failed: {ex.Message}");
            return D.EmptyView(G.Warning, Loc.T("This page could not be shown."));
        }
    }

    public override double Height(double width, double budget)
    {
        try
        {
            View.Measure(new Size(width, double.PositiveInfinity));
            return Math.Min(budget, Math.Max(80, View.DesiredSize.Height));
        }
        catch
        {
            return budget;
        }
    }
}
