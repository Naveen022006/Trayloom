// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>The tray flyout "Quick toggles" tab: the list of 11 rows with edit mode.</summary>
public sealed class QuickTogglesPanelSection : IPanelSection
{
    /// <inheritdoc />
    public IReadOnlyList<string> FeatureIds => ["quick-toggles", "mic-mute", "quit-apps"];

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public string TabId => PanelTabs.Toggles;

    /// <inheritdoc />
    public bool SupportsEditing => true;

    /// <inheritdoc />
    public FrameworkElement CreateView(IPanelContext context)
    {
        var list = new QuickTogglesListView(context, (action, delay) => context.InvokeAfterClose(action, delay), context.IsEditing);
        list.LayoutChanged += context.RequestRelayout;

        void OnEditing() => list.Editing = context.IsEditing;
        void OnReset() => list.ResetDefaults();
        void OnActive()
        {
            // Actions may activate other apps (Explorer refresh, confirmation dialogs): the flyout must not close on Deactivated.
            context.SetKeepOpen(context.IsActive);
            if (context.IsActive)
            {
                list.Refresh();
            }
        }

        list.Loaded += (_, _) =>
        {
            context.EditingChanged += OnEditing;
            context.ResetRequested += OnReset;
            context.ActiveChanged += OnActive;
            context.SetKeepOpen(true);
        };
        list.Unloaded += (_, _) =>
        {
            context.EditingChanged -= OnEditing;
            context.ResetRequested -= OnReset;
            context.ActiveChanged -= OnActive;
            context.SetKeepOpen(false);
        };
        return list;
    }
}
