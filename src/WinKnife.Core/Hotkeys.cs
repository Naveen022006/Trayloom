// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Core;

/// <summary>Modifier keys of a <see cref="Hotkey"/> (values match the RegisterHotKey MOD_* flags).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key combination such as <c>Ctrl+Alt+K</c>. Serialised as that string.</summary>
/// <param name="Modifiers">Held modifiers.</param>
/// <param name="Key">The main (non-modifier) key.</param>
[JsonConverter(typeof(HotkeyJsonConverter))]
public sealed record Hotkey(HotkeyModifiers Modifiers, Key Key)
{
    /// <summary>The Win32 virtual-key code of <see cref="Key"/>.</summary>
    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>Parses text like <c>Ctrl+Alt+K</c>, <c>Win+Shift+F5</c> or <c>Ctrl+Space</c>; throws <see cref="FormatException"/> on failure.</summary>
    public static Hotkey Parse(string text) =>
        TryParse(text, out var hotkey) ? hotkey : throw new FormatException($"'{text}' is not a valid hotkey.");

    /// <summary>Tries to parse text like <c>Ctrl+Alt+K</c>.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // "Ctrl++" style input: the last token is empty and the key is the plus key.
        var mods = HotkeyModifiers.None;
        Key? key = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctl":
                    mods |= HotkeyModifiers.Control;
                    continue;
                case "alt":
                    mods |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    mods |= HotkeyModifiers.Shift;
                    continue;
                case "win" or "windows" or "super" or "cmd":
                    mods |= HotkeyModifiers.Win;
                    continue;
            }

            if (key is not null || !TryParseKey(part, out var parsed))
            {
                return false;
            }

            key = parsed;
        }

        if (key is null)
        {
            return false;
        }

        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    /// <summary>Formats as <c>Ctrl+Alt+Shift+Win+Key</c>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            sb.Append("Win+");
        }

        sb.Append(KeyName(Key));
        return sb.ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.Return => "Enter",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Back => "Backspace",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };

    private static bool TryParseKey(string text, out Key key)
    {
        switch (text.ToLowerInvariant())
        {
            case "enter": key = Key.Return; return true;
            case "esc": key = Key.Escape; return true;
            case "pageup" or "pgup": key = Key.Prior; return true;
            case "pagedown" or "pgdn": key = Key.Next; return true;
            case "backspace": key = Key.Back; return true;
            case "del": key = Key.Delete; return true;
            case "ins": key = Key.Insert; return true;
            case "`": key = Key.OemTilde; return true;
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
        }

        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key is not (Key.None or Key.System or Key.DeadCharProcessed);
    }
}

internal sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Hotkey.TryParse(reader.GetString(), out var hotkey) ? hotkey : null;

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Thrown when a hotkey cannot be registered (usually because another program owns it).</summary>
public sealed class HotkeyRegistrationException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public HotkeyRegistrationException(Hotkey hotkey, int win32Error)
        : base($"Could not register {hotkey}: {new Win32Exception(win32Error).Message}")
    {
        Hotkey = hotkey;
        Win32Error = win32Error;
    }

    /// <summary>The hotkey that failed.</summary>
    public Hotkey Hotkey { get; }

    /// <summary>The Win32 error code (1409 = already registered).</summary>
    public int Win32Error { get; }
}

/// <summary>Global hotkeys (RegisterHotKey on a hidden message window).</summary>
public interface IHotkeyService
{
    /// <summary>
    /// Registers a global hotkey; <paramref name="callback"/> runs on the UI thread. Dispose the result to unregister.
    /// Throws <see cref="HotkeyRegistrationException"/> if the key combination is taken.
    /// </summary>
    IDisposable Register(Hotkey hotkey, Action callback);

    /// <summary>Like <see cref="Register"/> but returns <c>false</c> instead of throwing.</summary>
    bool TryRegister(Hotkey hotkey, Action callback, [NotNullWhen(true)] out IDisposable? registration);
}

/// <summary>Default <see cref="IHotkeyService"/>; must be created on the UI thread.</summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private sealed class Registration(HotkeyService owner, int id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unregister(id);
            }
        }
    }

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly Dictionary<int, Action> _callbacks = [];
    private HwndSource? _source;
    private int _nextId = 0x2000;

    /// <summary>Creates the service on the UI thread.</summary>
    public HotkeyService(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
    }

    /// <inheritdoc />
    public IDisposable Register(Hotkey hotkey, Action callback)
    {
        if (!TryRegisterCore(hotkey, callback, out var registration, out var error))
        {
            throw new HotkeyRegistrationException(hotkey, error);
        }

        return registration;
    }

    /// <inheritdoc />
    public bool TryRegister(Hotkey hotkey, Action callback, [NotNullWhen(true)] out IDisposable? registration)
    {
        var ok = TryRegisterCore(hotkey, callback, out var created, out var error);
        registration = created;
        if (!ok)
        {
            _log.Warn($"Hotkey {hotkey} could not be registered (Win32 error {error})");
        }

        return ok;
    }

    /// <summary>Unregisters everything and destroys the message window.</summary>
    public void Dispose()
    {
        if (_ui.HasShutdownFinished)
        {
            return;
        }

        _ui.Invoke(() =>
        {
            foreach (var id in _callbacks.Keys.ToArray())
            {
                NativeMethods.UnregisterHotKey(_source!.Handle, id);
            }

            _callbacks.Clear();
            _source?.Dispose();
            _source = null;
        });
    }

    private bool TryRegisterCore(Hotkey hotkey, Action callback, [NotNullWhen(true)] out Registration? registration, out int error)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var result = (Registration?)null;
        var err = 0;
        OnUi(() =>
        {
            EnsureWindow();
            var id = _nextId++;
            var mods = (uint)hotkey.Modifiers | MOD_NOREPEAT;
            if (NativeMethods.RegisterHotKey(_source!.Handle, id, mods, (uint)hotkey.VirtualKey))
            {
                _callbacks[id] = callback;
                result = new Registration(this, id);
            }
            else
            {
                err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            }
        });
        registration = result;
        error = err;
        return result is not null;
    }

    private void Unregister(int id) => OnUi(() =>
    {
        if (_source is not null && _callbacks.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }
    });

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess())
        {
            action();
        }
        else if (!_ui.HasShutdownStarted)
        {
            _ui.Invoke(action);
        }
    }

    private void EnsureWindow()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("WinKnife.Hotkeys")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _callbacks.TryGetValue((int)wParam, out var callback))
        {
            handled = true;
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                _log.Error("Hotkey callback failed", ex);
            }
        }

        return 0;
    }
}
