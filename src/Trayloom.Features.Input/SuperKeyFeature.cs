// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Input;

/// <summary>State of the super key shown on its settings page.</summary>
internal enum SuperKeyState
{
    Off,
    Working,
    Paused,
    HookFailed,
}

/// <summary>Published by the engine, read by the settings page (the two are separate instances).</summary>
internal static class SuperKeyStatus
{
    public static volatile SuperKeyState Current = SuperKeyState.Off;

    public static event Action? Changed;

    public static void Set(SuperKeyState state)
    {
        if (Current != state)
        {
            Current = state;
            Changed?.Invoke();
        }
    }
}

/// <summary>
/// "Turns one key into the modifier combination you choose." The chosen source key (Caps Lock, or the right-hand
/// Win / Alt / Ctrl / Shift) is swallowed; while it is held, the first other key (or mouse press) is replaced by an
/// atomic injection of the chosen modifiers followed by that key, and the modifiers are released when the source key
/// is released. A tap on its own (no other key) performs the chosen solo action instead: nothing, toggle Caps Lock,
/// switch input language (a long hold toggles Caps Lock) or press Escape. While any app of the "Apps to leave alone"
/// list is running, even in the background, the feature pauses and the key behaves normally.
/// Windows reports no physical state for swallowed keys (measured: GetAsyncKeyState stays 0), so the watchdog is
/// event-based: modifiers still held after 20 s without any keyboard or mouse event are released. A second check every
/// 500 ms does not wait for idleness: it also releases when the foreground window is missing or elevated, when the input
/// desktop is not the normal one (UAC, lock screen) and when the hook is lost; the session locking and Trayloom exiting
/// release too. While the feature is on a marker file lets the next start release modifiers a crash left down
/// (<see cref="SuperKeyRecovery"/>).
/// </summary>
public sealed class SuperKeyFeature : InputFeatureBase
{
    private const int HoldForCapsMs = 500;
    private const int WatchdogMs = 500;
    private const string MarkerName = "superkey-armed";

    private volatile int _source = 0x14;
    private volatile ushort[] _modifiers = [0xA2, 0xA4, 0xA0, 0x5B];
    private volatile string _solo = "none";
    private volatile bool _paused;
    private volatile string _sourceName = "capsLock";

    private bool _held;
    private bool _active;
    private bool _used;
    private long _downTick;
    private long _lastEvent;
    private ushort[] _activeModifiers = [];
    private System.Threading.Timer? _watchdog;
    private System.Threading.Timer? _pausePoll;
    private string? _markerPath;
    private int _watchdogBusy;
    private readonly SuperKeyForeground _foreground = new();

    public override string Id => "super-key";

    protected override bool IsEnabled() => App.Get<bool>(K.SuperEnabled);

    protected override void Engage()
    {
        _held = false;
        _active = false;
        SubscribeKeyboard(OnKey, HookPriorities.SuperKey);
        SubscribeMouse(OnMouse, HookPriorities.SuperKey);
        ArmMarker();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _pausePoll = new System.Threading.Timer(_ => PollPause(), null, 0, 1500);
        _watchdog = new System.Threading.Timer(_ => Watchdog(), null, WatchdogMs, WatchdogMs);
        SuperKeyStatus.Set(SuperKeyState.Working);
        var hooks = Context!.Hooks;
        _ = Task.Delay(500).ContinueWith(_ =>
        {
            // The hook is installed on its own thread; if Windows refused it the settings page shows the error row.
            if (IsEngaged && !hooks.KeyboardHookInstalled)
            {
                SuperKeyStatus.Set(SuperKeyState.HookFailed);
                ReleaseNow();
            }
        });
    }

    protected override void Disengage()
    {
        _pausePoll?.Dispose();
        _pausePoll = null;
        _watchdog?.Dispose();
        _watchdog = null;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        ReleaseNow();
        DisarmMarker();
        SuperKeyStatus.Set(SuperKeyState.Off);
    }

    protected override void LoadSettings()
    {
        _sourceName = App.Get<string>(K.SuperSource) ?? "capsLock";
        _source = _sourceName switch
        {
            "rightCommand" => 0x5C,
            "rightOption" => 0xA5,
            "rightControl" => 0xA3,
            "rightShift" => 0xA1,
            _ => 0x14,
        };
        _modifiers = ParseModifiers(App.Get<string>(K.SuperModifiers));
        _solo = App.Get<string>(K.SuperSolo) ?? "none";
    }

    /// <summary>The virtual keys of a stored modifier string (<c>control+option+shift+command</c>); invalid text gives all four.</summary>
    internal static ushort[] ParseModifiers(string? text)
    {
        var list = new List<ushort>(4);
        var tokens = (text ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "control":
                    list.Add(0xA2);
                    break;
                case "option":
                    list.Add(0xA4);
                    break;
                case "shift":
                    list.Add(0xA0);
                    break;
                case "command":
                    list.Add(0x5B);
                    break;
            }
        }

        var hasPrimary = list.Any(v => v is 0xA2 or 0xA4 or 0x5B);
        return hasPrimary ? [.. list.Distinct()] : [0xA2, 0xA4, 0xA0, 0x5B];
    }

    private bool IsSource(KeyboardHookEvent e)
    {
        var source = _source;
        return source switch
        {
            0xA5 => e.VkCode == 0xA5 || (e.VkCode == 0x12 && e.IsExtended),
            0xA3 => e.VkCode == 0xA3 || (e.VkCode == 0x11 && e.IsExtended),
            _ => e.VkCode == source,
        };
    }

    private static bool IsModifierVk(int vk) => vk is >= 0x10 and <= 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromTrayloom || _paused)
        {
            return false;
        }

        Volatile.Write(ref _lastEvent, Environment.TickCount64);
        INPUT[]? release;
        Action? solo;
        bool swallow;
        lock (this)
        {
            swallow = HandleKey(e, out release, out solo);
        }

        // Injecting can block on a slow foreground app: never while holding the lock.
        Flush(release, solo);
        return swallow;
    }

    // Caller holds the lock.
    private bool HandleKey(KeyboardHookEvent e, out INPUT[]? release, out Action? solo)
    {
        release = null;
        solo = null;
        {
            // AltGr layouts send a fake left Ctrl with scan code 0x21D before Right Alt: it must not leak.
            if (_source == 0xA5 && e.VkCode == 0xA2 && e.ScanCode == 0x21D)
            {
                return true;
            }

            if (IsSource(e))
            {
                if (e.IsKeyDown)
                {
                    if (!_held)
                    {
                        _held = true;
                        _active = false;
                        _used = false;
                        _downTick = Environment.TickCount64;
                    }

                    return true; // swallow the press and its auto-repeat
                }

                if (_held)
                {
                    release = TakeRelease(solo: true, out solo);
                }

                return true;
            }

            if (!_held || !e.IsKeyDown || IsModifierVk(e.VkCode))
            {
                return false;
            }

            _used = true;
            if (_active)
            {
                return false; // modifiers already down: let the key through
            }

            _active = true;
            _activeModifiers = _modifiers;
            var inputs = new List<INPUT>(_activeModifiers.Length + 1);
            foreach (var modifier in _activeModifiers)
            {
                inputs.Add(KeySender.KeyInput(modifier, false));
            }

            // Re-send the key itself so it is guaranteed to arrive after the modifiers.
            var key = KeySender.KeyInput((ushort)e.VkCode, false);
            if (e.IsExtended)
            {
                key.u.ki.dwFlags |= NativeConstants.KEYEVENTF_EXTENDEDKEY;
            }

            inputs.Add(key);
            InputSender.Send([.. inputs]);
            return true;
        }
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (!_held || _paused || e.IsFromTrayloom || !e.IsButtonDown)
        {
            return false;
        }

        Volatile.Write(ref _lastEvent, Environment.TickCount64);
        lock (this)
        {
            if (!_held)
            {
                return false;
            }

            _used = true;
            if (_active)
            {
                return false;
            }

            var button = e.Kind switch
            {
                MouseEventKind.LeftDown => System.Windows.Input.MouseButton.Left,
                MouseEventKind.RightDown => System.Windows.Input.MouseButton.Right,
                MouseEventKind.MiddleDown => System.Windows.Input.MouseButton.Middle,
                _ => e.XButton == 1 ? System.Windows.Input.MouseButton.XButton1 : System.Windows.Input.MouseButton.XButton2,
            };
            _active = true;
            _activeModifiers = _modifiers;
            var inputs = new List<INPUT>(_activeModifiers.Length + 1);
            foreach (var modifier in _activeModifiers)
            {
                inputs.Add(KeySender.KeyInput(modifier, false));
            }

            inputs.Add(KeySender.ButtonInput(button, true));
            InputSender.Send([.. inputs]);
            return true;
        }
    }

    /// <summary>
    /// Caller holds the lock. Resets the state and returns the modifier key-ups to inject (null when none are down); the caller
    /// sends them, and runs <paramref name="soloAction"/>, after leaving the lock.
    /// </summary>
    private INPUT[]? TakeRelease(bool solo, out Action? soloAction)
    {
        soloAction = null;
        if (!_held && !_active)
        {
            return null;
        }

        var wasUsed = _used;
        var heldMs = Environment.TickCount64 - _downTick;
        _held = false;
        INPUT[]? inputs = null;
        if (_active)
        {
            var modifiers = _activeModifiers;
            inputs = new INPUT[modifiers.Length];
            for (var i = 0; i < modifiers.Length; i++)
            {
                inputs[i] = KeySender.KeyInput(modifiers[^(i + 1)], true);
            }

            _active = false;
            _activeModifiers = [];
        }

        if (solo && !wasUsed)
        {
            var mode = _solo;
            soloAction = () => RunSolo(mode, heldMs);
        }

        return inputs;
    }

    private static void Flush(INPUT[]? release, Action? solo)
    {
        try
        {
            if (release is { Length: > 0 })
            {
                InputSender.Send(release);
            }

            solo?.Invoke();
        }
        catch
        {
            // best effort: the next watchdog tick releases again if modifiers are still tracked
        }
    }

    /// <summary>Releases anything held (no solo action), sending outside the lock. Safe from any thread.</summary>
    private void ReleaseNow()
    {
        INPUT[]? release;
        lock (this)
        {
            release = TakeRelease(solo: false, out _);
        }

        Flush(release, null);
    }

    private static void RunSolo(string mode, long heldMs)
    {
        switch (mode)
        {
            case "capsLock":
                KeySender.Tap(0x14);
                break;
            case "inputSource":
                if (heldMs >= HoldForCapsMs)
                {
                    KeySender.Tap(0x14); // a long hold restores Caps Lock
                }
                else
                {
                    KeySender.SendChord([0x5B, 0x20]); // Win+Space switches the input language
                }

                break;
            case "escape":
                KeySender.Tap(0x1B);
                break;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff)
        {
            ReleaseNow();
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        ReleaseNow();
        DisarmMarker();
    }

    private void ArmMarker()
    {
        try
        {
            _markerPath = Path.Combine(Context!.DataDirectory, MarkerName);
            AtomicFile.WriteAllText(_markerPath, "armed");
        }
        catch (Exception ex)
        {
            Context?.Log.Warn($"super key: could not write the crash marker: {ex.Message}");
        }
    }

    private void DisarmMarker()
    {
        try
        {
            if (_markerPath is not null)
            {
                File.Delete(_markerPath);
            }
        }
        catch
        {
            // the next start cleans it up
        }
    }

    // Runs on a timer thread, never on the hook thread: the foreground checks call into other processes.
    private void Watchdog()
    {
        if (Interlocked.Exchange(ref _watchdogBusy, 1) == 1)
        {
            return;
        }

        try
        {
            bool active;
            lock (this)
            {
                active = _active;
            }

            if (!active)
            {
                return;
            }

            var idleMs = Environment.TickCount64 - Volatile.Read(ref _lastEvent);
            var hooks = Context?.Hooks;
            var hookLost = hooks is null || !hooks.KeyboardHookInstalled;
            var snapshot = _foreground.Sample();
            if (hookLost || SuperKeyMath.ShouldRelease(true, idleMs, snapshot.Missing, snapshot.Elevated, snapshot.InputDesktopNotDefault, false))
            {
                ReleaseNow();
            }
        }
        catch
        {
            // a failed probe must not stop the watchdog
        }
        finally
        {
            Volatile.Write(ref _watchdogBusy, 0);
        }
    }

    // Any running app of the list pauses the feature (even in the background).
    private void PollPause()
    {
        try
        {
            var ids = (App.Get<List<string>?>(K.SuperExceptions) ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var paused = false;
            if (ids.Count > 0)
            {
                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        string? path = null;
                        try
                        {
                            path = AppIdentity.OfProcess((uint)process.Id);
                        }
                        catch
                        {
                            // inaccessible
                        }

                        var name = path ?? (process.ProcessName + ".exe").ToLowerInvariant();
                        if (ids.Any(id => AppIdentity.Matches(id, name)))
                        {
                            paused = true;
                            break;
                        }
                    }
                }
            }

            if (paused != _paused)
            {
                _paused = paused;
                if (paused)
                {
                    ReleaseNow();
                }

                SuperKeyStatus.Set(paused ? SuperKeyState.Paused : SuperKeyState.Working);
            }
        }
        catch
        {
            // best effort
        }
    }
}
