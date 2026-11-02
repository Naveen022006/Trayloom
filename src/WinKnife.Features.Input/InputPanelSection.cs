// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>Services captured when the first Input feature starts (panel sections have no context before they are built).</summary>
internal static class InputHost
{
    public static volatile IAppSettings? App;

    public static volatile IFeatureRuntime? Runtime;
}

/// <summary>
/// The "Mouse and keyboard" category of the tray panel's Controls tab: one switch row per installed feature, in the
/// original order (invert scrolling, linear scrolling, focus follows mouse, mouse acceleration, side buttons, key
/// debounce, text snippets, mouse buttons, super key, click debounce). Captions are tooltips; in edit mode every row
/// gets an eye button (<c>panelControl*</c> keys). Rows of features that are not installed do not exist.
/// </summary>
public sealed class InputControlsPanelSection : IPanelSection
{
    private sealed record Item(string Id, string FeatureId, string VisibleKey, string Glyph, string Title, string Caption,
        Func<IAppSettings, bool> IsOn, Action<IAppSettings, bool> Set, string? Settings = null);

    private static readonly Item[] Items =
    [
        new("mouseScroll", "scroll-inverter", "panelControlMouseScroll", "", Strings.InvertMouseScrolling, Strings.InvertCaptionPanel,
            a => a.Get<bool>(K.ScrollInvertV) || a.Get<bool>(K.ScrollInvertH),
            (a, on) =>
            {
                a.Set(K.ScrollInvertV, on);
                a.Set(K.ScrollInvertH, on);
            }),
        new("linearScroll", "linear-scroll", "panelControlLinearScroll", "", Strings.LinearTitle, Strings.LinearCaption,
            a => a.Get<bool>(K.LinearEnabled), (a, on) => a.Set(K.LinearEnabled, on)),
        new("focusFollowsMouse", "focus-follows-mouse", "panelControlFocusFollowsMouse", "", Strings.FocusTitle, Strings.FocusCaption,
            a => a.Get<bool>(K.FocusEnabled), (a, on) => a.Set(K.FocusEnabled, on)),
        new("mouseAcceleration", "mouse-acceleration", "panelControlMouseAcceleration", "", Strings.AccelTitle, Strings.AccelCaption,
            a => a.Get<bool>(K.AccelDisabled), (a, on) => a.Set(K.AccelDisabled, on)),
        new("mouseNavigation", "mouse-navigation", "panelControlMouseNavigation", "", Strings.UseSideButtons, Strings.UseSideButtonsCaption,
            a => a.Get<bool>(K.NavEnabled), (a, on) => a.Set(K.NavEnabled, on)),
        new("keyDebounce", "keyboard-debounce", "panelControlKeyDebounce", "", Strings.DebounceTitle, Strings.FilterDuplicateKeysCaptionShort,
            a => a.Get<bool>(K.KeyDebounceEnabled), (a, on) => a.Set(K.KeyDebounceEnabled, on)),
        new("textSnippets", "text-snippets", "panelControlTextSnippets", "", Strings.SnippetsTitle, Strings.ExpandWhileTypingCaption,
            a => a.Get<bool>(K.SnippetsEnabled), (a, on) => a.Set(K.SnippetsEnabled, on), SettingsPages.TextSnippets),
        new("mouseButtonShortcuts", "mouse-button-shortcuts", "panelControlMouseButtonShortcuts", "", Strings.MouseButtonsPanel, Strings.MouseButtonsPanelCaption,
            a => a.Get<bool>(K.ButtonShortcutsEnabled), (a, on) => a.Set(K.ButtonShortcutsEnabled, on), "mouse#mouseButtonShortcuts"),
        new("superKey", "super-key", "panelControlSuperKey", "", Strings.SuperKeyTitle, string.Empty,
            a => a.Get<bool>(K.SuperEnabled), (a, on) => a.Set(K.SuperEnabled, on), SettingsPages.SuperKey),
        new("mouseClickDebounce", "mouse-click-debounce", "panelControlMouseClickDebounce", "", Strings.MouseClickDebounce, Strings.ClickFilterCaption,
            a => a.Get<bool>(K.ClickDebounceEnabled), (a, on) => a.Set(K.ClickDebounceEnabled, on)),
    ];

    public IReadOnlyList<string> FeatureIds => [.. Items.Select(i => i.FeatureId)];

    public int Order => 100;

    public string TabId => PanelTabs.Controls;

    public bool SupportsEditing => true;

    public string? Category => PanelTabs.CategoryInput;

    public (int Enabled, int Total) CategoryCounts
    {
        get
        {
            var app = InputHost.App;
            var runtime = InputHost.Runtime;
            if (app is null || runtime is null)
            {
                return (0, 0);
            }

            var rows = Items.Where(i => runtime.IsAvailable(i.FeatureId) && app.Get<bool>(i.VisibleKey, true)).ToList();
            return (rows.Count(i => i.IsOn(app)), rows.Count);
        }
    }

    public FrameworkElement CreateView(IPanelContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var group = new PanelRowGroup();
        foreach (var item in Items)
        {
            if (!c.Runtime.IsAvailable(item.FeatureId))
            {
                continue;
            }

            var visible = app.Get<bool>(item.VisibleKey, true);
            if (!visible && !c.IsEditing)
            {
                continue;
            }

            group.Add(Row(c, item, visible));
        }

        if (group.RowCount == 0)
        {
            return new PanelCard(UiKit.Text(Strings.NoControls, 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        }

        c.ResetRequested += () =>
        {
            foreach (var item in Items)
            {
                app.Set(item.VisibleKey, true);
            }

            c.RequestRelayout();
        };
        return group;
    }

    private static FrameworkElement Row(IPanelContext c, Item item, bool visible)
    {
        var app = c.App;
        var caption = item.Id == "superKey" ? SuperKeyCaption(app) : item.Caption;
        var stack = new StackPanel();
        var toggle = new PanelToggleRow(item.Glyph, item.Title, caption, item.IsOn(app), on =>
        {
            item.Set(app, on);
            if (item.Id is "keyDebounce" or "superKey")
            {
                c.RequestRelayout();
            }
        });
        if (item.Id == "superKey")
        {
            toggle.ToolTip = caption;
        }

        FrameworkElement main = toggle;
        if (c.IsEditing)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toggle.Opacity = visible ? 1 : 0.45;
            toggle.IsEnabled = false;
            grid.Children.Add(toggle);
            var eye = new Button { Content = UiKit.Icon(visible ? "" : "", 13, ThemeKeys.TextSecondary), Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0), ToolTip = visible ? Strings.Hide : Strings.Show };
            eye.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            eye.Click += (_, _) =>
            {
                app.Set(item.VisibleKey, !visible);
                c.RequestRelayout();
            };
            Grid.SetColumn(eye, 1);
            grid.Children.Add(eye);
            main = grid;
        }

        stack.Children.Add(main);

        if (!c.IsEditing && item.IsOn(app))
        {
            if (item.Id == "keyDebounce")
            {
                var window = Math.Clamp(app.Get<int>(K.KeyDebounceWindow), 0, 500);
                var row = new Grid { Margin = new Thickness(41, 0, 10, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(UiKit.Text(Strings.GlobalWindow, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
                var stepper = InputUi.Stepper(0, 500, window, v => $"{v} ms", v => app.Set(K.KeyDebounceWindow, v));
                Grid.SetColumn(stepper, 1);
                row.Children.Add(stepper);
                stack.Children.Add(row);
            }
            else if (item.Settings is not null)
            {
                var button = PanelButtons.Mini(Strings.Manage, null, () =>
                {
                    c.ClosePanel();
                    c.InvokeAfterClose(() => c.Shell.OpenSettings(item.Settings));
                });
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(41, 0, 10, 8);
                stack.Children.Add(button);
            }
        }

        return stack;
    }

    private static string SuperKeyCaption(IAppSettings app)
    {
        var source = app.Get<string>(K.SuperSource) switch
        {
            "rightCommand" => Strings.RightWin,
            "rightOption" => Strings.RightAlt,
            "rightControl" => Strings.RightCtrl,
            "rightShift" => Strings.RightShift,
            _ => Strings.CapsLock,
        };
        var mods = SuperKeyFeature.ParseModifiers(app.Get<string>(K.SuperModifiers));
        var names = new List<string>();
        if (mods.Contains((ushort)0xA2))
        {
            names.Add(Strings.Ctrl);
        }

        if (mods.Contains((ushort)0xA4))
        {
            names.Add(Strings.Alt);
        }

        if (mods.Contains((ushort)0xA0))
        {
            names.Add(Strings.Shift);
        }

        if (mods.Contains((ushort)0x5B))
        {
            names.Add(Strings.Win);
        }

        return $"{source} {string.Join("+", names)}";
    }
}
