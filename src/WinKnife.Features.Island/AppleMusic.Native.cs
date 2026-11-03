// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace WinKnife.Features.Island;

/// <summary>Win32 interop of the Apple Music driver (window styles, placement, WinEvent hooks, CreateProcess).</summary>
internal static class AmNative
{
    internal delegate bool EnumProc(nint hwnd, nint lParam);

    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const int SwMinimize = 6;
    internal const int SwShowMinNoActive = 7;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const int GwlExStyle = -20;
    internal const long WsExLayered = 0x80000;
    internal const long WsExTransparent = 0x20;
    internal const uint LwaAlpha = 2;
    internal const long WsExToolWindow = 0x80;
    internal const long WsExNoActivate = 0x08000000;
    internal const uint GwOwner = 4;
    internal const uint EventSystemForeground = 3;
    internal const uint EventObjectCreate = 0x8000;
    internal const uint EventObjectShow = 0x8002;
    internal const uint GaRoot = 2;
    internal const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect Normal;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public nint Reserved3;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumProc proc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    internal static extern void SwitchToThisWindow(nint hwnd, bool altTab);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string? application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref StartupInfo info, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint handle);

    /// <summary>The rectangle (virtual screen) shows at least a little of the desktop.</summary>
    internal static bool OnAnyScreen(Rect r)
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        return r.Right > left + 40 && r.Left < left + width - 40 && r.Bottom > top + 40 && r.Top < top + height - 40;
    }

    /// <summary>The window is on another virtual desktop (or otherwise cloaked by DWM).</summary>
    internal static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, 14, out var cloaked, 4) == 0 && cloaked != 0;

    /// <summary>The window is fully transparent and click-through (how the driver works on Apple Music's windows), so nobody sees it.</summary>
    internal static bool IsGhost(nint hwnd)
    {
        var ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        if ((ex & WsExLayered) == 0 || (ex & WsExTransparent) == 0)
        {
            return false;
        }

        return GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & LwaAlpha) != 0 && alpha == 0;
    }

    /// <summary>Whether the process is Apple Music (looked up by process name).</summary>
    internal static bool IsAppleMusic(uint pid)
    {
        if (pid == 0)
        {
            return false;
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("AppleMusic", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Apple Music's main windows (not its popups).</summary>
    internal static List<nint> MainWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (ClassOf(h) == "WinUIDesktopWin32WindowClass" && IsAppleMusic(ProcessOf(h)))
            {
                list.Add(h);
            }

            return true;
        }, 0);
        return list;
    }

    /// <summary>True when Apple Music has no window the user can see: none at all, or all minimised, hidden, parked off-screen or transparent.</summary>
    internal static bool MainWindowsHidden()
    {
        foreach (var h in MainWindows())
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsGhost(h))
            {
                continue;
            }

            GetWindowRect(h, out var rect);
            if (OnAnyScreen(rect))
            {
                return false;
            }
        }

        return true;
    }

    internal static string ClassOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetClassNameW(hwnd, s, 256);
        return s.ToString();
    }

    internal static string TitleOf(nint hwnd)
    {
        var s = new StringBuilder(256);
        GetWindowTextW(hwnd, s, 256);
        return s.ToString();
    }

    internal static uint ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    internal static List<nint> TopLevelWindows(uint pid)
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (ProcessOf(h) == pid)
            {
                list.Add(h);
            }

            return true;
        }, 0);
        return list;
    }

    internal static string Describe(nint hwnd)
    {
        if (hwnd == 0)
        {
            return "(none)";
        }

        if (!IsWindow(hwnd))
        {
            return $"0x{hwnd:X} (gone)";
        }

        string name;
        try
        {
            name = System.Diagnostics.Process.GetProcessById((int)ProcessOf(hwnd)).ProcessName;
        }
        catch
        {
            name = "?";
        }

        return $"0x{hwnd:X} {name}/{ClassOf(hwnd)} '{TitleOf(hwnd)}'";
    }
}

/// <summary>A WinEvent hook (create / show) on its own message-loop thread; the callback runs on that thread.</summary>
internal sealed class AmEventHook : IDisposable
{
    private readonly AmNative.WinEventProc _proc;
    private readonly Thread _thread;
    private uint _threadId;

    public AmEventHook(uint minEvent, uint maxEvent, uint processId, Action<uint, nint> callback)
    {
        _proc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == 0)
            {
                return;
            }

            try
            {
                callback(ev, hwnd);
            }
            catch
            {
                // a vanished window must never break the hook thread
            }
        };
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = AmNative.GetCurrentThreadId();
            var hook = AmNative.SetWinEventHook(minEvent, maxEvent, 0, _proc, processId, 0, 0);
            ready.Set();
            while (AmNative.GetMessage(out _, 0, 0, 0) > 0)
            {
            }

            if (hook != 0)
            {
                AmNative.UnhookWinEvent(hook);
            }
        })
        {
            IsBackground = true,
            Name = "WinKnife AppleMusic hook",
        };
        _thread.Start();
        ready.Wait();
    }

    public void Dispose()
    {
        AmNative.PostThreadMessage(_threadId, AmNative.WmQuit, 0, 0);
        _thread.Join(1000);
    }
}

/// <summary>
/// Keeps Apple Music from taking the activation while WinKnife drives it in the background. A system-wide foreground WinEvent hook
/// (on its own message-loop thread) watches for any Apple Music window becoming the foreground window; when that happens while no
/// Apple Music window is visible to the user, the foreground goes back to the window the user was working in and Apple Music's main
/// window is minimised again without activation. Armed at the start of a job and kept for <see cref="GraceMs"/> after <see cref="Dispose"/>.
/// No input is ever synthesised.
/// </summary>
internal sealed class ForegroundGuard : IDisposable
{
    internal const int GraceMs = 1500;

    private readonly Action<string> _say;
    private readonly bool _enforce;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly object _gate = new();
    private nint _target;
    private int _reclaims;
    private AmEventHook? _hook;

    private ForegroundGuard(nint previous, Action<string>? say)
    {
        _say = say ?? (_ => { });

        // When the user has Apple Music open on screen it is none of our business who has the foreground.
        _enforce = AmNative.MainWindowsHidden();
        _target = PickTarget(previous);
        if (_enforce)
        {
            _hook = new AmEventHook(AmNative.EventSystemForeground, AmNative.EventSystemForeground, 0, (_, hwnd) =>
            {
                var pid = AmNative.ProcessOf(hwnd);
                if (pid != _ownPid && AmNative.IsAppleMusic(pid))
                {
                    Reclaim("Apple Music took the foreground (" + AmNative.Describe(hwnd) + ")", true);
                }
            });
        }
    }

    /// <summary>Starts guarding. <paramref name="previous"/> is the foreground window of the moment (it is only used as the target when it is neither WinKnife's nor Apple Music's).</summary>
    public static ForegroundGuard Arm(nint previous, Action<string>? say = null) => new(previous, say);

    /// <summary>Gives the foreground back now when Apple Music (or nothing) has it.</summary>
    public void Restore() => Reclaim("restore", false);

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, null);
        if (hook is not null)
        {
            _ = Task.Delay(GraceMs).ContinueWith(_ => hook.Dispose(), TaskScheduler.Default);
        }
    }

    private bool Usable(nint hwnd) =>
        hwnd != 0 && AmNative.IsWindow(hwnd) && AmNative.IsWindowVisible(hwnd) && !AmNative.IsIconic(hwnd) && !AmNative.IsCloaked(hwnd)
        && AmNative.ProcessOf(hwnd) is var pid && pid != _ownPid && !AmNative.IsAppleMusic(pid);

    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"];

    /// <summary>The window that should have the foreground: <paramref name="previous"/> when it is the user's own, else the top real application window.</summary>
    private nint PickTarget(nint previous)
    {
        if (Usable(previous))
        {
            return previous;
        }

        nint found = 0;
        AmNative.EnumWindows((h, _) =>
        {
            if (!Usable(h) || AmNative.GetWindow(h, AmNative.GwOwner) != 0 || AmNative.TitleOf(h).Length == 0)
            {
                return true;
            }

            var ex = (long)AmNative.GetWindowLongPtr(h, AmNative.GwlExStyle);
            if ((ex & (AmNative.WsExToolWindow | AmNative.WsExNoActivate)) != 0 || Array.IndexOf(ShellClasses, AmNative.ClassOf(h)) >= 0)
            {
                return true;
            }

            AmNative.GetWindowRect(h, out var r);
            if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50)
            {
                return true;
            }

            found = h;
            return false;
        }, 0);
        return found;
    }

    private void Reclaim(string why, bool counted)
    {
        if (!_enforce)
        {
            return;
        }

        lock (_gate)
        {
            if (counted && ++_reclaims > 20)
            {
                return;
            }

            var now = AmNative.GetForegroundWindow();
            var fromAppleMusic = now != 0 && AmNative.IsAppleMusic(AmNative.ProcessOf(now));
            if (now != 0 && !fromAppleMusic)
            {
                return;
            }

            if (!Usable(_target))
            {
                _target = PickTarget(0);
            }

            if (_target != 0 && now != _target)
            {
                var ok = Focus(_target);
                _say($"{why}; foreground back to {AmNative.Describe(_target)} => {ok}; foreground now {AmNative.Describe(AmNative.GetForegroundWindow())}");
            }

            foreach (var h in AmNative.MainWindows())
            {
                if (AmNative.IsWindowVisible(h) && !AmNative.IsIconic(h) && !AmNative.IsGhost(h))
                {
                    AmNative.ShowWindow(h, AmNative.SwShowMinNoActive);
                    _say("Apple Music main window had been restored; minimised again without activation");
                }
            }
        }
    }

    private static bool HasForeground(nint target)
    {
        var f = AmNative.GetForegroundWindow();
        return f != 0 && (f == target || AmNative.GetAncestor(f, AmNative.GaRoot) == target);
    }

    private static bool Focus(nint target)
    {
        AmNative.SetForegroundWindow(target);
        if (HasForeground(target))
        {
            return true;
        }

        // Windows only lets the foreground owner (or a thread sharing its input queue) hand the activation on: join the owner's input queue for a moment.
        var fg = AmNative.GetForegroundWindow();
        var fgThread = fg != 0 ? AmNative.GetWindowThreadProcessId(fg, out _) : 0;
        var me = AmNative.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && AmNative.AttachThreadInput(me, fgThread, true);
        try
        {
            AmNative.SetForegroundWindow(target);
            AmNative.BringWindowToTop(target);
        }
        finally
        {
            if (attached)
            {
                AmNative.AttachThreadInput(me, fgThread, false);
            }
        }

        if (HasForeground(target))
        {
            return true;
        }

        AmNative.SwitchToThisWindow(target, true);
        return HasForeground(target);
    }
}
