// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.App;

/// <summary>One entry of the Settings history.</summary>
internal sealed record Visit(string Page, string? Anchor, string? SidebarFeature)
{
    public string Destination => SettingsPages.Join(Page, Anchor);
}

/// <summary>
/// The Settings router (spec 02-settings 1.2): current page + anchor, Back / Forward history, a one-shot feature target for the
/// Features hub. Every request publishes a fresh <see cref="RequestId"/> so repeats re-fire.
/// </summary>
internal sealed class SettingsRouter
{
    private readonly List<Visit> _history = [];
    private int _index = -1;
    private readonly Func<Visit, bool> _isVisible;

    public SettingsRouter(Func<Visit, bool> isVisible)
    {
        _isVisible = isVisible;
    }

    public event Action? Navigated;

    public Visit? Current => _index >= 0 ? _history[_index] : null;

    public Guid RequestId { get; private set; }

    /// <summary>One-shot: the feature row to reveal inside the Features hub.</summary>
    public string? PendingFeatureTarget { get; set; }

    public bool CanGoBack => FindVisible(-1) >= 0;

    public bool CanGoForward => FindVisible(1) >= 0;

    /// <summary>Navigates to <c>page#anchor</c>.</summary>
    public void Request(string destination, string? targetFeature = null, string? sidebarFeature = null, bool replacingVisit = false)
    {
        var (page, anchor) = SettingsPages.Split(destination);
        if (page == SettingsPages.Island && anchor == "companion")
        {
            // Companion routes to the Dynamic Island page with the Companion tab flag (kept as the anchor).
        }

        var visit = new Visit(page, anchor, sidebarFeature);
        PendingFeatureTarget = targetFeature;
        var current = Current;
        if (replacingVisit && current is not null)
        {
            _history[_index] = visit;
        }
        else if (current is not null && current.Page == page && current.Anchor == anchor)
        {
            _history[_index] = visit;
        }
        else if (current is not null && current.Page == page && anchor is not null && page is not (SettingsPages.General or SettingsPages.Energy))
        {
            // A section-anchor request on the same page only refines the current entry.
            _history[_index] = visit;
        }
        else
        {
            if (_index < _history.Count - 1)
            {
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            }

            _history.Add(visit);
            _index = _history.Count - 1;
        }

        Publish();
    }

    public void Back() => Move(-1);

    public void Forward() => Move(1);

    /// <summary>Called after install state changed: leave a page whose gating features are all gone.</summary>
    public void EnsureVisible()
    {
        var current = Current;
        if (current is not null && !_isVisible(current))
        {
            Request(SettingsPages.Features, replacingVisit: true);
        }
    }

    private void Move(int direction)
    {
        var target = FindVisible(direction);
        if (target < 0)
        {
            return;
        }

        _index = target;
        PendingFeatureTarget = null;
        Publish();
    }

    private int FindVisible(int direction)
    {
        for (var i = _index + direction; i >= 0 && i < _history.Count; i += direction)
        {
            if (_isVisible(_history[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private void Publish()
    {
        RequestId = Guid.NewGuid();
        Navigated?.Invoke();
    }
}
