// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>
/// Keeps parts of a settings view in sync with the flat settings while the view is on screen: blocks that exist only
/// "when on" are shown / collapsed live (no page rebuild, so the scroll position never jumps), and other widgets can
/// register a refresh callback.
/// </summary>
internal sealed class LiveView
{
    private readonly IAppSettings _app;
    private readonly IFeatureRuntime _runtime;
    private readonly Dispatcher _ui;
    private readonly List<(UIElement Element, Func<bool> Visible)> _items = [];
    private readonly List<Action> _refreshers = [];
    private bool _subscribed;

    public LiveView(IContributionServices services)
    {
        _app = services.App;
        _runtime = services.Runtime;
        _ui = services.Ui;
    }

    /// <summary>Shows <paramref name="element"/> only while <paramref name="visible"/> returns true.</summary>
    public T Show<T>(T element, Func<bool> visible) where T : UIElement
    {
        element.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed;
        _items.Add((element, visible));
        return element;
    }

    /// <summary>Registers a callback run (UI thread) on every settings change while attached.</summary>
    public void OnChange(Action refresh) => _refreshers.Add(refresh);

    /// <summary>Attaches to the root: subscribes while it is loaded.</summary>
    public T Attach<T>(T root) where T : FrameworkElement
    {
        root.Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                _subscribed = true;
                _app.Changed += OnChanged;
                _runtime.Changed += Refresh;
            }

            Refresh();
        };
        root.Unloaded += (_, _) =>
        {
            if (_subscribed)
            {
                _subscribed = false;
                _app.Changed -= OnChanged;
                _runtime.Changed -= Refresh;
            }
        };
        return root;
    }

    private void OnChanged(string key) => _ui.BeginInvoke(Refresh);

    private void Refresh()
    {
        foreach (var (element, visible) in _items)
        {
            try
            {
                element.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                // keep the previous state
            }
        }

        foreach (var refresh in _refreshers)
        {
            try
            {
                refresh();
            }
            catch
            {
                // keep the previous state
            }
        }
    }
}

/// <summary>Row builders shared by the Input settings sections.</summary>
internal static class Rows
{
    public const double Inset = SettingsRow.TextInset;

    /// <summary>A row with a switch bound to a boolean flat key.</summary>
    public static SettingsRow Toggle(IAppSettings app, string? glyph, string title, string? caption, string key, Action<bool>? also = null, SwitchSize size = SwitchSize.Regular)
    {
        var toggle = new MacSwitch(app.Get<bool>(key), size, on =>
        {
            app.Set(key, on);
            also?.Invoke(on);
        });
        return new SettingsRow(glyph, title, caption, toggle);
    }

    /// <summary>A follow-up block indented under a row (spacing 10 between children).</summary>
    public static StackPanel Indented(double spacing = 10, double left = Inset)
    {
        return new StackPanel { Margin = new Thickness(left, 0, 0, 0), Tag = spacing };
    }

    /// <summary>Appends a child to an indented block with the block's spacing.</summary>
    public static void AddTo(StackPanel block, UIElement child)
    {
        var spacing = block.Tag is double d ? d : 10;
        block.AddSpaced(child, spacing);
    }

    /// <summary>A title (subheadline, medium) used for sub-groups.</summary>
    public static System.Windows.Controls.TextBlock SubTitle(string text) =>
        UiKit.Text(text, 12, FontWeights.Medium, ThemeKeys.TextPrimary);
}
