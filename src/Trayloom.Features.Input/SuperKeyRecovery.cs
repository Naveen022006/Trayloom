// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Input;

/// <summary>The pure release decision of the super key watchdog.</summary>
internal static class SuperKeyMath
{
    /// <summary>Idle time after which injected modifiers are released.</summary>
    public const long IdleLimitMs = 20_000;

    /// <summary>
    /// True when modifiers are down (<paramref name="active"/>) and any reason to let go applies: idle too long, no foreground window,
    /// an elevated foreground window (Windows blocks our key-ups there), a secure or non-default input desktop, or a locked session.
    /// </summary>
    public static bool ShouldRelease(bool active, long idleMs, bool foregroundMissing, bool foregroundElevated, bool inputDesktopNotDefault, bool sessionLocked) =>
        active && (idleMs > IdleLimitMs || foregroundMissing || foregroundElevated || inputDesktopNotDefault || sessionLocked);
}

/// <summary>What the watchdog sees about the foreground window and the input desktop.</summary>
internal readonly record struct ForegroundSnapshot(bool Missing, bool Elevated, bool InputDesktopNotDefault);

/// <summary>Probes the foreground window and the input desktop for the watchdog. Never call it on the hook thread.</summary>
internal sealed class SuperKeyForeground
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int UserObjectName = 2;
    private const uint DesktopReadObjects = 0x0001;

    private static readonly bool s_selfElevated = ElevationPolicy.IsElevated;
    private readonly Dictionary<uint, bool> _elevatedByPid = [];
    private long _cacheStamp;

    public ForegroundSnapshot Sample()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        var missing = hwnd == 0;
        var elevated = false;
        if (!missing && !s_selfElevated)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            elevated = IsElevatedProcess(pid);
        }

        return new ForegroundSnapshot(missing, elevated, InputDesktopNotDefault());
    }

    private bool IsElevatedProcess(uint pid)
    {
        var now = Environment.TickCount64;
        if (now - _cacheStamp > 30_000 || _elevatedByPid.Count > 256)
        {
            // Process ids are reused: forget old answers now and then.
            _elevatedByPid.Clear();
            _cacheStamp = now;
        }

        if (_elevatedByPid.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var result = QueryElevated(pid);
        _elevatedByPid[pid] = result;
        return result;
    }

    // A process whose token cannot be opened is treated as elevated: that is what a higher integrity level looks like to us.
    private static bool QueryElevated(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return true;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return true;
            }

            try
            {
                return !GetTokenInformation(token, TokenElevationClass, out int elevation, sizeof(int), out _) || elevation != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // The secure desktop (UAC prompt, lock screen) cannot be opened by a normal process: a failure counts as "not default".
    private static bool InputDesktopNotDefault()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == 0)
        {
            return true;
        }

        try
        {
            var buffer = new char[64];
            if (!GetUserObjectInformationW(desktop, UserObjectName, buffer, buffer.Length * sizeof(char), out var needed) || needed < sizeof(char))
            {
                return true;
            }

            var name = new string(buffer, 0, (int)Math.Min(needed / sizeof(char), (uint)buffer.Length)).TrimEnd('\0');
            return !name.Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint obj, int index, [Out] char[] info, int length, out uint needed);
}

/// <summary>
/// After a crash or kill the super key may have left Ctrl, Alt, Shift or Win pressed in Windows. While the feature runs it keeps a
/// marker file; if the marker is still there at the next start, this hook injects key-ups (key-ups only, through Core's
/// <see cref="InputSender"/>) for the modifiers Windows still reports as down.
/// </summary>
public sealed class SuperKeyRecovery : IStartupRecovery
{
    private static readonly ushort[] Modifiers = [0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1, 0x5B, 0x5C];

    public string Id => "super-key";

    public void Recover(RecoveryStage stage, ILog log)
    {
        if (stage is not (RecoveryStage.Startup or RecoveryStage.Crash))
        {
            return;
        }

        var marker = Path.Combine(AppPaths.FeatureData("super-key"), "superkey-armed");
        if (!File.Exists(marker))
        {
            return;
        }

        var inputs = new List<INPUT>();
        foreach (var vk in Modifiers)
        {
            if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                inputs.Add(KeySender.KeyInput(vk, true));
            }
        }

        if (inputs.Count > 0)
        {
            InputSender.Send([.. inputs]);
            log.Info($"super key: released {inputs.Count} modifier(s) left down by a previous run");
        }

        if (stage == RecoveryStage.Startup)
        {
            try
            {
                File.Delete(marker);
            }
            catch
            {
                // harmless: the next start tries again
            }
        }
    }
}
