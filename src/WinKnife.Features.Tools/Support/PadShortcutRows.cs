// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Services captured when a Tools feature starts, for stateless contribution classes (utilities) that have no context.</summary>
internal static class PadServices
{
    /// <summary>The services of the last started pad-project feature (null before any started).</summary>
    public static IContributionServices? Ctx { get; set; }

    /// <summary>Text of the currently registered shortcut of a role (null while inactive).</summary>
    public static string? ShortcutOf(string roleId) => Ctx is null ? null : WinKnife.Features.Tools.ShortcutText.Of(Ctx, roleId);
}

/// <summary>The "Global shortcut" toggle + recorder rows of the Quick tools cards (Vorssaint <c>ShortcutPreferenceRow</c>).</summary>
internal static class ShortcutSettingsRows
{
    public static void AddGlobalShortcut(SettingsCard card, IContributionServices c, string roleId, string enableKey, string toggleTitle = "Global shortcut")
    {
        var status = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        var recorder = new HotkeyRecorder { Hotkey = c.Shortcuts.GetHotkey(roleId), Width = 150, IsEnabled = c.App.Get<bool>(enableKey) };
        var reset = UiKit.Button("Reset", ButtonKind.Bordered, 12);
        string? error = null;

        void Refresh()
        {
            var enabled = c.App.Get<bool>(enableKey);
            var state = c.Shortcuts.GetState(roleId);
            string? text = error;
            if (text is null && enabled)
            {
                text = state.Problem ?? (state.IsRegistered ? null : "Windows rejected this shortcut. Choose another one.");
            }

            status.Text = text ?? string.Empty;
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            recorder.IsEnabled = enabled;
            reset.IsEnabled = enabled && !c.Shortcuts.IsDefault(roleId);
        }

        card.Add(new SettingsRow(null, toggleTitle, null, new MacSwitch(c.App.Get<bool>(enableKey), SwitchSize.Regular, on =>
        {
            c.App.Set(enableKey, on);
            error = null;
            Refresh();
        })));
        recorder.HotkeyChanged += (_, hotkey) =>
        {
            var result = c.Shortcuts.TrySet(roleId, hotkey);
            error = result.Ok ? null : result.Error;
            if (!result.Ok)
            {
                recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            }

            Refresh();
        };
        reset.Click += (_, _) =>
        {
            c.Shortcuts.Reset(roleId);
            recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            error = null;
            Refresh();
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(recorder);
        reset.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(reset);
        card.Add(new SettingsRow(null, "Shortcut", null, right));
        card.Add(status);
        Refresh();
        void OnChanged(string id)
        {
            if (id == roleId)
            {
                card.Dispatcher.BeginInvoke(Refresh);
            }
        }

        c.Shortcuts.Changed += OnChanged;
        card.Unloaded += (_, _) => c.Shortcuts.Changed -= OnChanged;
    }
}
