// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Trayloom.Core.Ui;

/// <summary>
/// Text box that records a key combination. Click it, press the shortcut; Backspace/Delete clears it and
/// Escape cancels. Bind or observe <see cref="Hotkey"/> / <see cref="HotkeyChanged"/>.
/// </summary>
public class HotkeyRecorder : TextBox
{
    /// <summary>The recorded hotkey (null = none).</summary>
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(Hotkey),
        typeof(HotkeyRecorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyChanged));

    /// <summary>Allows a bare key without a modifier (Vorssaint's bare-key recorders). Default false: only F-keys are accepted bare.</summary>
    public bool AllowBareKey { get; set; }

    /// <summary>Creates the recorder.</summary>
    public HotkeyRecorder()
    {
        SetResourceReference(StyleProperty, ThemeKeys.HotkeyRecorderStyle);
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        MinWidth = 150;
        IsUndoEnabled = false;
        ContextMenu = null;
        Refresh();
    }

    /// <summary>Raised when the user records or clears a hotkey.</summary>
    public event EventHandler<Hotkey?>? HotkeyChanged;

    /// <summary>The recorded hotkey.</summary>
    public Hotkey? Hotkey
    {
        get => (Hotkey?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = Loc.T("Press keys");
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        switch (key)
        {
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed or Key.ImeProcessed:
                return;
            case Key.Escape:
                Keyboard.ClearFocus();
                return;
            case Key.Back or Key.Delete when Keyboard.Modifiers == ModifierKeys.None:
                Commit(null);
                return;
        }

        var mods = HotkeyModifiers.None;
        var held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control))
        {
            mods |= HotkeyModifiers.Control;
        }

        if (held.HasFlag(ModifierKeys.Alt))
        {
            mods |= HotkeyModifiers.Alt;
        }

        if (held.HasFlag(ModifierKeys.Shift))
        {
            mods |= HotkeyModifiers.Shift;
        }

        if (held.HasFlag(ModifierKeys.Windows))
        {
            mods |= HotkeyModifiers.Win;
        }

        // A bare letter/digit would hijack normal typing system-wide; require a modifier except for F-keys.
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (mods == HotkeyModifiers.None && !isFunctionKey && !AllowBareKey)
        {
            return;
        }

        Commit(new Hotkey(mods, key));
    }

    private static void OnHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyRecorder)d).Refresh();

    private void Commit(Hotkey? hotkey)
    {
        Hotkey = hotkey;
        HotkeyChanged?.Invoke(this, hotkey);
        Keyboard.ClearFocus();
        Refresh();
    }

    private void Refresh()
    {
        if (!IsKeyboardFocused)
        {
            Text = Hotkey?.ToString() ?? Loc.T("None");
        }
    }
}
