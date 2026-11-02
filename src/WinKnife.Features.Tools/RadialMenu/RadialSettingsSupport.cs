// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>What changed in the radial menu editor state.</summary>
internal enum RadialChange
{
    Enabled,
    Profiles,
    Items,
}

/// <summary>
/// Editing state shared by the sections of the Radial menu settings page (the sections are separate stateless classes):
/// selected profile, submenu drill-down trail and the mutation helpers that load, edit, sanitise and save the profiles.
/// </summary>
internal static class RadialState
{
    /// <summary>Services of the running feature (null while it is not running).</summary>
    public static IContributionServices? Ctx { get; set; }

    public static string? SelectedId { get; set; }

    public static List<string> Trail { get; } = [];

    public static bool ListOpen { get; set; }

    public static event Action<RadialChange>? Changed;

    public static void Raise(RadialChange change) => Changed?.Invoke(change);

    public static List<RadialProfile> Profiles(IContributionServices ctx) => RadialModel.Load(ctx);

    public static RadialProfile Selected(IContributionServices ctx, List<RadialProfile>? profiles = null)
    {
        profiles ??= Profiles(ctx);
        var found = profiles.FirstOrDefault(p => p.Id == SelectedId);
        if (found is null)
        {
            found = profiles[0];
            SelectedId = found.Id;
            Trail.Clear();
        }

        return found;
    }

    /// <summary>The list of the level the user is looking at (root or a submenu), following <see cref="Trail"/>.</summary>
    public static List<RadialItem> Level(RadialProfile profile)
    {
        var list = profile.Items;
        for (var i = 0; i < Trail.Count; i++)
        {
            var next = list.FirstOrDefault(x => x.Id == Trail[i] && x.Kind == RadialKinds.Submenu);
            if (next is null)
            {
                Trail.RemoveRange(i, Trail.Count - i);
                break;
            }

            list = next.Children;
        }

        return list;
    }

    /// <summary>Display name of the submenu the trail ends in (null at root).</summary>
    public static string? SubmenuName(RadialProfile profile)
    {
        if (Trail.Count == 0)
        {
            return null;
        }

        var list = profile.Items;
        RadialItem? item = null;
        foreach (var id in Trail)
        {
            item = list.FirstOrDefault(x => x.Id == id);
            if (item is null)
            {
                return null;
            }

            list = item.Children;
        }

        return item is null ? null : (item.Name.Length > 0 ? item.Name : "Submenu");
    }

    /// <summary>Loads, edits, sanitises and stores the profiles; then raises <paramref name="change"/> (null = silent).</summary>
    public static void Mutate(IContributionServices ctx, Action<List<RadialProfile>, RadialProfile> edit, RadialChange? change)
    {
        var profiles = Profiles(ctx);
        var selected = Selected(ctx, profiles);
        edit(profiles, selected);
        RadialModel.Save(ctx, profiles);
        if (change is { } c)
        {
            Raise(c);
        }
    }

    public static void MutateLevel(IContributionServices ctx, Action<List<RadialItem>> edit, RadialChange change = RadialChange.Items) =>
        Mutate(ctx, (_, profile) => edit(Level(profile)), change);
}

/// <summary>A section view that rebuilds itself when the shared state changes.</summary>
internal sealed class RadialLiveView : Border
{
    private readonly Func<UIElement> _build;
    private readonly Func<RadialChange, bool> _filter;

    public RadialLiveView(Func<UIElement> build, Func<RadialChange, bool> filter)
    {
        _build = build;
        _filter = filter;
        Child = build();
        Loaded += (_, _) =>
        {
            RadialState.Changed -= OnChanged;
            RadialState.Changed += OnChanged;
        };
        Unloaded += (_, _) => RadialState.Changed -= OnChanged;
    }

    public void Rebuild() => Child = _build();

    private void OnChanged(RadialChange change)
    {
        if (_filter(change))
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
    }
}

/// <summary>Small modal dialogs of the radial menu editor.</summary>
internal static class RadialDialogs
{
    public static Window NewDialog(Window? owner, string title, double width)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = owner,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        return window;
    }

    /// <summary>Confirmation with a destructive or prominent button and Cancel.</summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool destructive)
    {
        var window = NewDialog(owner, title, 380);
        var result = false;
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(UiKit.Text(title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        var text = UiKit.Text(message, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        text.Margin = new Thickness(0, 8, 0, 18);
        stack.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = UiKit.Button("Cancel", ButtonKind.Bordered, 12.5, onClick: window.Close);
        var ok = UiKit.Button(confirmText, destructive ? ButtonKind.Destructive : ButtonKind.Prominent, 12.5, onClick: () =>
        {
            result = true;
            window.Close();
        });
        ok.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        window.Content = stack;
        window.ShowDialog();
        return result;
    }

    public static TextBox NewTextBox(string text, double width = 0)
    {
        var box = new TextBox { Text = text, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        if (width > 0)
        {
            box.Width = width;
        }

        return box;
    }

    /// <summary>A text box with a grey placeholder shown while empty.</summary>
    public static FrameworkElement WithPlaceholder(TextBox box, string placeholder)
    {
        var grid = new Grid();
        grid.Children.Add(box);
        var hint = UiKit.Text(placeholder, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(8, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(hint);
        void Sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        return grid;
    }
}
