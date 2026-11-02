// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Input;

/// <summary>Sends key chords through <see cref="InputSender"/> (tagged, so WinKnife hooks ignore them).</summary>
internal static class KeySender
{
    /// <summary>Virtual-key codes of a hotkey: modifiers first, then the main key.</summary>
    public static ushort[] ToChord(Hotkey hotkey)
    {
        var keys = new List<ushort>(5);
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            keys.Add(0x11);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            keys.Add(0x12);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            keys.Add(0x10);
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            keys.Add((ushort)VK_LWIN);
        }

        keys.Add((ushort)hotkey.VirtualKey);
        return [.. keys];
    }

    /// <summary>Presses the keys in order and releases them in reverse, marking navigation keys as extended.</summary>
    public static void SendChord(ushort[] keys)
    {
        if (keys.Length == 0)
        {
            return;
        }

        var inputs = new INPUT[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            var flags = IsExtended(keys[i]) ? KEYEVENTF_EXTENDEDKEY : 0u;
            inputs[i] = Key(keys[i], flags);
            inputs[inputs.Length - 1 - i] = Key(keys[i], flags | KEYEVENTF_KEYUP);
        }

        InputSender.Send(inputs);
    }

    /// <summary>Presses and releases one key.</summary>
    public static void Tap(ushort key) => SendChord([key]);

    /// <summary>Sends key-down (or key-up) events for several keys at once.</summary>
    public static void SendKeys(IReadOnlyList<ushort> keys, bool keyUp)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var inputs = new INPUT[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            var flags = (IsExtended(keys[i]) ? KEYEVENTF_EXTENDEDKEY : 0u) | (keyUp ? KEYEVENTF_KEYUP : 0u);
            inputs[i] = Key(keys[i], flags);
        }

        InputSender.Send(inputs);
    }

    /// <summary>
    /// Types an expansion: normal characters as Unicode key events, line breaks as Enter and tabs as Tab.
    /// </summary>
    public static void TypeText(string text)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var atEnd = i == text.Length;
            if (!atEnd && text[i] is not ('\n' or '\t' or '\r'))
            {
                continue;
            }

            if (i > start)
            {
                InputSender.SendText(text[start..i]);
            }

            if (!atEnd)
            {
                switch (text[i])
                {
                    case '\n':
                        Tap((ushort)Win32.VK_RETURN);
                        break;
                    case '\t':
                        Tap((ushort)Win32.VK_TAB);
                        break;
                }
            }

            start = i + 1;
        }
    }

    public static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } },
    };
}

/// <summary>
/// Shared engine of side-buttons and mouse-button-shortcuts: maps middle / XButton1 / XButton2 to key chords,
/// swallowing both the press and the release of a mapped button.
/// </summary>
internal sealed class MouseButtonRemapper
{
    // Index 0 = middle, 1 = XButton1, 2 = XButton2. Replaced atomically by LoadSettings.
    private volatile ushort[]?[] _chords = new ushort[]?[3];
    private readonly bool[] _swallowUp = new bool[3];

    public void Set(ushort[]? middle, ushort[]? x1, ushort[]? x2) => _chords = [middle, x1, x2];

    /// <summary>Hook-thread handler; true when the event was consumed.</summary>
    public bool Handle(MouseHookEvent e)
    {
        if (e.IsFromWinKnife || e.IsInjected)
        {
            return false;
        }

        int index;
        switch (e.Kind)
        {
            case MouseEventKind.MiddleDown or MouseEventKind.MiddleUp:
                index = 0;
                break;
            case MouseEventKind.XButtonDown or MouseEventKind.XButtonUp:
                index = e.XButton == 1 ? 1 : 2;
                break;
            default:
                return false;
        }

        if (e.IsButtonDown)
        {
            var chord = _chords[index];
            if (chord is null)
            {
                return false;
            }

            _swallowUp[index] = true;
            KeySender.SendChord(chord);
            return true;
        }

        if (_swallowUp[index])
        {
            _swallowUp[index] = false;
            return true;
        }

        return false;
    }
}
