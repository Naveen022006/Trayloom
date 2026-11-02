// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Shortcut recorder rows built on <see cref="IShortcutService"/> (conflicts, Reset, clear, recording hint, Windows problems).</summary>
internal static class ShortcutUi
{
    /// <summary>Recorder (width 108) + optional clear (x) + Reset button; <paramref name="note"/> is the orange line to put under the row.</summary>
    public static StackPanel Accessory(IShortcutService shortcuts, string roleId, bool clearable, bool enabled, out TextBlock note, Action? changed = null)
    {
        var recorder = new HotkeyRecorder { Hotkey = shortcuts.GetHotkey(roleId), Width = 136, IsEnabled = enabled };
        var noteBlock = FormUi.Warning(string.Empty);
        noteBlock.Visibility = Visibility.Collapsed;
        noteBlock.Margin = new Thickness(0, 4, 0, 0);
        var clear = clearable ? SmallIconButton("", Strings.RemoveShortcut) : null;
        var reset = UiKit.Button(Strings.Reset, ButtonKind.Bordered, 11.5);
        reset.Margin = new Thickness(6, 0, 0, 0);

        void Refresh()
        {
            recorder.Hotkey = shortcuts.GetHotkey(roleId);
            if (clear is not null)
            {
                clear.IsEnabled = enabled && recorder.Hotkey is not null;
                clear.Opacity = clear.IsEnabled ? 1 : 0.4;
            }

            reset.IsEnabled = enabled && !shortcuts.IsDefault(roleId);
            var problem = shortcuts.GetState(roleId).Problem;
            if (noteBlock.Visibility != Visibility.Visible || problem is not null)
            {
                noteBlock.Text = problem ?? string.Empty;
                noteBlock.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        recorder.GotKeyboardFocus += (_, _) =>
        {
            noteBlock.Text = Loc.F("{0} Escape cancels. Delete clears.", Strings.ShortcutRecording);
            noteBlock.Visibility = Visibility.Visible;
        };
        recorder.LostKeyboardFocus += (_, _) =>
        {
            noteBlock.Visibility = Visibility.Collapsed;
            Refresh();
        };
        recorder.HotkeyChanged += (_, hotkey) =>
        {
            var result = shortcuts.TrySet(roleId, hotkey);
            Refresh();
            if (!result.Ok)
            {
                noteBlock.Text = result.Error ?? string.Empty;
                noteBlock.Visibility = Visibility.Visible;
            }

            changed?.Invoke();
        };
        if (clear is not null)
        {
            clear.Click += (_, _) =>
            {
                shortcuts.TrySet(roleId, null);
                Refresh();
                changed?.Invoke();
            };
        }

        reset.Click += (_, _) =>
        {
            shortcuts.Reset(roleId);
            Refresh();
            changed?.Invoke();
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(recorder);
        if (clear is not null)
        {
            panel.Children.Add(clear);
        }

        panel.Children.Add(reset);
        note = noteBlock;
        Refresh();
        return panel;
    }

    /// <summary>A shortcut row: icon tile + label on the left, recorder on the right, note below.</summary>
    public static FrameworkElement LabelledRow(IShortcutService shortcuts, string roleId, string? glyph, string label, bool enabled = true, bool clearable = false)
    {
        var accessory = Accessory(shortcuts, roleId, clearable, enabled, out var note);
        var stack = new StackPanel();
        stack.Children.Add(new SettingsRow(glyph, label, null, accessory));
        note.Margin = new Thickness(glyph is null ? 0 : SettingsRow.TextInset, 4, 0, 0);
        stack.Children.Add(note);
        return stack;
    }

    public static Button SmallIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = UiKit.Icon(glyph, 13, ThemeKeys.TextSecondary),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, tooltip);
        return button;
    }
}
