// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Tools;

/// <summary>Vorssaint's <c>CleaningUnlockCounter</c>: five Escape presses, each within 6 s of the previous one, unlock.</summary>
internal sealed class CleaningUnlockCounter
{
    public const int Threshold = 5;
    public const long PressWindowMs = 6000;
    public const int VkEscape = 0x1B;

    private long _lastEscape = -1;

    /// <summary>Current progress 0..5.</summary>
    public int Progress { get; private set; }

    /// <summary>Registers a key-down. Auto-repeat never counts or resets; any key but Escape resets. True when unlocked.</summary>
    public bool RegisterKeyDown(int code, long timeMs, bool isRepeat)
    {
        if (isRepeat)
        {
            return false;
        }

        if (code != VkEscape)
        {
            Reset();
            return false;
        }

        Progress = _lastEscape >= 0 && timeMs - _lastEscape <= PressWindowMs ? Progress + 1 : 1;
        _lastEscape = timeMs;
        return Progress >= Threshold;
    }

    /// <summary>A modifier release or other non-Escape event: resets the sequence.</summary>
    public void RegisterOther() => Reset();

    /// <summary>Clears the progress.</summary>
    public void Reset()
    {
        Progress = 0;
        _lastEscape = -1;
    }

    /// <summary>True for modifier and media/system keys (they reset the sequence on press and release).</summary>
    public static bool IsModifierOrMedia(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x14 or 0x5F
        || vk is >= 0xA6 and <= 0xB7;
}

/// <summary>Vorssaint's <c>CleaningMouseReleaseGate</c>: a user-requested unlock waits until every pressed mouse button was released.</summary>
internal sealed class CleaningMouseReleaseGate
{
    public const int ReleaseWaitMs = 5000;

    private readonly HashSet<int> _pressed = [];
    private bool _pending;

    /// <summary>True while an unlock waits for releases.</summary>
    public bool Pending => _pending;

    /// <summary>A button went down.</summary>
    public void ButtonDown(int button) => _pressed.Add(button);

    /// <summary>A button went up; true when this release completes a pending deactivation.</summary>
    public bool ButtonUp(int button)
    {
        _pressed.Remove(button);
        if (_pending && _pressed.Count == 0)
        {
            _pending = false;
            return true;
        }

        return false;
    }

    /// <summary>Requests deactivation; true when it can proceed now.</summary>
    public bool RequestDeactivation()
    {
        if (_pressed.Count == 0)
        {
            _pending = false;
            return true;
        }

        _pending = true;
        return false;
    }

    /// <summary>The hook was blind: forget presses but keep the request. True when the pending request can proceed.</summary>
    public bool InvalidateTrackedPresses()
    {
        _pressed.Clear();
        if (_pending)
        {
            _pending = false;
            return true;
        }

        return false;
    }

    /// <summary>The 5 s wait expired: forget presses; true when a request was pending.</summary>
    public bool ReleaseWaitExpired()
    {
        _pressed.Clear();
        var was = _pending;
        _pending = false;
        return was;
    }
}
