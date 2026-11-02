// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using WinKnife.Core;

namespace WinKnife.Features.Input;

/// <summary>
/// "Extra click filter": ignores rapid extra clicks from worn mouse buttons without slowing normal clicks. Only the
/// left, right and middle buttons are filtered. A healthy click always passes immediately; after an accepted Up, a new
/// Down inside the filter window (5-100 ms, default 25) is suppressed together with its own Up; the Up of an accepted
/// Down is never suppressed. State resets when time goes backwards and when the filter stops, so no button can stay
/// held. Events injected by other programs are never filtered.
/// </summary>
public sealed class ClickDebounceFeature : InputFeatureBase
{
    // Index: 0 left, 1 right, 2 middle. Hook-thread only.
    private readonly uint[] _lastUp = new uint[3];
    private readonly bool[] _hasUp = new bool[3];
    private readonly bool[] _down = new bool[3];
    private readonly bool[] _suppressed = new bool[3];

    private volatile int _windowMs = 25;

    public override string Id => "click-debounce";

    protected override bool IsEnabled() => App.Get<bool>(K.ClickDebounceEnabled);

    protected override void Engage()
    {
        Reset();
        SubscribeMouse(OnMouse, HookPriorities.ClickDebounce);
    }

    protected override void Disengage() => Reset();

    protected override void LoadSettings()
    {
        var window = App.Get<int>(K.ClickDebounceWindow);
        _windowMs = window is >= 5 and <= 100 ? window : 25;
    }

    private void Reset()
    {
        Array.Clear(_hasUp);
        Array.Clear(_down);
        Array.Clear(_suppressed);
    }

    private static int ButtonIndex(MouseHookEvent e) => e.Kind switch
    {
        MouseEventKind.LeftDown or MouseEventKind.LeftUp => 0,
        MouseEventKind.RightDown or MouseEventKind.RightUp => 1,
        MouseEventKind.MiddleDown or MouseEventKind.MiddleUp => 2,
        _ => -1,
    };

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsInjected || !(e.IsButtonDown || e.IsButtonUp))
        {
            return false;
        }

        var index = ButtonIndex(e);
        if (index < 0)
        {
            return false;
        }

        if (e.IsButtonUp)
        {
            if (_suppressed[index])
            {
                _suppressed[index] = false;
                return true; // the Up of a suppressed Down
            }

            _down[index] = false;
            _hasUp[index] = true;
            _lastUp[index] = e.Timestamp;
            return false;
        }

        if (_suppressed[index])
        {
            return true; // repeated Downs while held are noise
        }

        if (_down[index])
        {
            return false; // a Down for a button we already accepted: leave it
        }

        if (_hasUp[index])
        {
            var elapsed = unchecked(e.Timestamp - _lastUp[index]);
            if (elapsed > int.MaxValue)
            {
                _hasUp[index] = false; // time went backwards
            }
            else if (elapsed < (uint)_windowMs)
            {
                _suppressed[index] = true;
                return true;
            }
        }

        _down[index] = true;
        return false;
    }
}

/// <summary>
/// "Filter duplicate keys": ignores a key-down that arrives within a few milliseconds of the previous press of the same
/// key (keyboard switch chatter). Per key (spec 02b 8.3): auto-repeat is ignored; a key-down is suppressed when (a) the
/// key is still down and its last accepted press was less than the window ago, or (b) the key was the last accepted key
/// and its previous release was less than the window ago; a window of 0 never filters that key; key-ups always pass;
/// state resets after a 5 s gap and on disable. The window is the global one (0-500 ms, default 5) unless the key has
/// its own entry in <c>keyboardDebounceKeyWindows</c> (<c>vk:ms,vk:ms</c>). Only WinKnife's own injected keys skip the filter.
/// </summary>
public sealed class KeyDebounceFeature : InputFeatureBase
{
    private const double StaleMs = 5000;

    private readonly double[] _lastPress = new double[256];
    private readonly double[] _lastRelease = new double[256];
    private readonly double[] _lastEvent = new double[256];
    private readonly bool[] _isDown = new bool[256];
    private readonly bool[] _swallowed = new bool[256];
    private int _lastAccepted = -1;

    private volatile int _windowMs = 5;
    private volatile int[] _perKey = NewTable();

    public override string Id => "key-debounce";

    protected override bool IsEnabled() => App.Get<bool>(K.KeyDebounceEnabled);

    protected override void Engage()
    {
        Reset();
        SubscribeKeyboard(OnKey, HookPriorities.KeyDebounce);
    }

    protected override void Disengage() => Reset();

    protected override void LoadSettings()
    {
        var window = App.Get<int>(K.KeyDebounceWindow);
        _windowMs = window is >= 0 and <= 500 ? window : 5;
        _perKey = ParseKeyWindows(App.Get<string>(K.KeyDebounceKeys));
    }

    /// <summary>Parses <c>vk:ms,vk:ms</c> (unparseable entries dropped, windows clamped to 0..500).</summary>
    internal static Dictionary<int, int> ParseOverrides(string? text)
    {
        var map = new Dictionary<int, int>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2 && int.TryParse(pieces[0], out var vk) && int.TryParse(pieces[1], out var ms) && vk is > 0 and < 256)
            {
                map[vk] = Math.Clamp(ms, 0, 500);
            }
        }

        return map;
    }

    internal static string FormatOverrides(IDictionary<int, int> map) =>
        string.Join(",", map.OrderBy(p => p.Key).Select(p => $"{p.Key}:{Math.Clamp(p.Value, 0, 500)}"));

    private static int[] NewTable()
    {
        var table = new int[256];
        Array.Fill(table, -1);
        return table;
    }

    private static int[] ParseKeyWindows(string? text)
    {
        var table = NewTable();
        foreach (var (vk, ms) in ParseOverrides(text))
        {
            table[vk] = ms;
        }

        return table;
    }

    private void Reset()
    {
        Array.Clear(_isDown);
        Array.Clear(_swallowed);
        Array.Clear(_lastPress);
        Array.Clear(_lastRelease);
        Array.Clear(_lastEvent);
        _lastAccepted = -1;
    }

    private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    private bool OnKey(KeyboardHookEvent e)
    {
        var vk = e.VkCode;
        if (e.IsFromWinKnife || (uint)vk > 255)
        {
            return false;
        }

        var now = NowMs();
        if (_lastEvent[vk] != 0 && now - _lastEvent[vk] > StaleMs && !_isDown[vk])
        {
            _lastPress[vk] = 0;
            _lastRelease[vk] = 0;
        }

        _lastEvent[vk] = now;

        if (e.IsKeyUp)
        {
            _isDown[vk] = false;
            _lastRelease[vk] = now;
            if (_swallowed[vk])
            {
                _swallowed[vk] = false;
                return true; // the release of a suppressed press (never lets a lone key-up through)
            }

            return false;
        }

        if (_isDown[vk])
        {
            return _swallowed[vk]; // auto-repeat follows the fate of its press
        }

        var perKey = _perKey[vk];
        var window = perKey >= 0 ? perKey : _windowMs;
        _isDown[vk] = true;
        if (window == 0)
        {
            Accept(vk, now);
            return false;
        }

        // A key-down for a key that is still down is auto-repeat (handled above: LL hooks carry no repeat flag), so the
        // only chatter case left is a re-press shortly after the release of the last accepted key.
        var chatter = _lastRelease[vk] != 0 && _lastAccepted == vk && now - _lastRelease[vk] < window;
        if (chatter)
        {
            _swallowed[vk] = true;
            return true;
        }

        Accept(vk, now);
        return false;
    }

    private void Accept(int vk, double now)
    {
        _lastPress[vk] = now;
        _lastAccepted = vk;
    }
}
