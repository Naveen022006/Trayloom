// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows.Input;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Core;

/// <summary>
/// SendInput wrapper. Everything sent through it carries <see cref="WinKnifeExtraInfo"/> in dwExtraInfo so
/// hook handlers can recognise (and ignore) WinKnife's own synthetic input via <c>IsFromWinKnife</c>.
/// </summary>
public static class InputSender
{
    /// <summary>Marker stored in dwExtraInfo of every injected event ("WKNF").</summary>
    public static readonly nuint WinKnifeExtraInfo = 0x574B4E46;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>Injects a vertical (or horizontal) wheel movement at the current cursor position.</summary>
    /// <param name="delta">Wheel delta; 120 equals one notch, positive scrolls up/right.</param>
    /// <param name="horizontal">True for the horizontal wheel.</param>
    public static bool SendWheel(int delta, bool horizontal = false) =>
        Send(MakeMouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta))) == 1;

    /// <summary>Injects a relative mouse move.</summary>
    public static bool SendMouseMove(int dx, int dy) => Send(MakeMouse(MOUSEEVENTF_MOVE, dx, dy, 0)) == 1;

    /// <summary>Presses or releases a mouse button.</summary>
    public static bool SendMouseButton(MouseButton button, bool down)
    {
        var (flag, data) = button switch
        {
            MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
            MouseButton.Right => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
            MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.XButton1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 1u),
            _ => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 2u),
        };
        return Send(MakeMouse(flag, 0, 0, data)) == 1;
    }

    /// <summary>Clicks (press + release) a mouse button.</summary>
    public static void SendClick(MouseButton button)
    {
        SendMouseButton(button, true);
        SendMouseButton(button, false);
    }

    /// <summary>Presses or releases a key by virtual-key code.</summary>
    public static bool SendKey(ushort virtualKey, bool keyUp = false, bool extended = false) =>
        Send(MakeKey(virtualKey, 0, (keyUp ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0))) == 1;

    /// <summary>Presses the given keys in order, then releases them in reverse order (a chord such as Ctrl+C).</summary>
    public static void SendChord(params ushort[] virtualKeys)
    {
        var inputs = new INPUT[virtualKeys.Length * 2];
        for (var i = 0; i < virtualKeys.Length; i++)
        {
            inputs[i] = MakeKey(virtualKeys[i], 0, 0);
            inputs[inputs.Length - 1 - i] = MakeKey(virtualKeys[i], 0, KEYEVENTF_KEYUP);
        }

        Send(inputs);
    }

    /// <summary>Types text as Unicode key events (layout independent).</summary>
    public static void SendText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(MakeKey(0, ch, KEYEVENTF_UNICODE));
            inputs.Add(MakeKey(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        Send([.. inputs]);
    }

    /// <summary>Sends raw INPUT structures, stamping each with <see cref="WinKnifeExtraInfo"/>. Returns the number injected.</summary>
    public static uint Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return 0;
        }

        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i].type == INPUT_MOUSE)
            {
                inputs[i].u.mi.dwExtraInfo = WinKnifeExtraInfo;
            }
            else
            {
                inputs[i].u.ki.dwExtraInfo = WinKnifeExtraInfo;
            }
        }

        return NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static INPUT MakeMouse(uint flags, int dx, int dy, uint data) => new()
    {
        type = INPUT_MOUSE,
        u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = data } },
    };

    private static INPUT MakeKey(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };
}
