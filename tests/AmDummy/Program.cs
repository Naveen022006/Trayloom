// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace AmDummy;

/// <summary>
/// H2 stand-in processes for the window guardian tests (nothing here touches the real Apple Music).
/// <c>--role owner</c>: plays Trayloom (records its pid and start time, then starts the guardian launcher like the real client does).
/// <c>--role windows --mode kept|ghost|parked|nearoff|iconicoff|hiddenNoRecord|helperRecord|maximised|maximisedVisible</c>: creates a window of
/// Apple Music's class and titles, puts its OWN window into the bad state, writes the state file, and logs how its windows look until it is
/// killed. <c>--rehide &lt;file&gt;</c>: when that file appears the window hides itself again and the state file is rewritten with THIS process as
/// its owner (a newer Trayloom that has taken the window over after the old one was killed).
/// </summary>
public static class Program
{
    private const string MainClass = "WinUIDesktopWin32WindowClass";
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";
    private const long ExLayered = 0x80000;
    private const long ExTransparent = 0x20;
    private const long ExToolWindow = 0x80;

    private static readonly WndProc Proc = DefWindowProcW;
    private static nint _main;
    private static nint _helper;
    private static nint _popup;
    private static string? _rehide;
    private static string _status = "";
    private static string _lastLine = "";
    private static RECT _good;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    public static int Main(string[] args)
    {
        string? Arg(string name, int offset = 1)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + offset < args.Length ? args[i + offset] : null;
        }

        _rehide = Arg("--rehide");
        return Arg("--role") switch
        {
            "owner" => Owner(Arg("--exe")!, Arg("--go")!, Arg("--info")!),
            "windows" => Windows(Arg("--mode")!, uint.Parse(Arg("--owner")!, CultureInfo.InvariantCulture), long.Parse(Arg("--owner", 2)!, CultureInfo.InvariantCulture), Arg("--ready")!, Arg("--status")!),
            _ => 2,
        };
    }

    // ---------------------------------------------------------------- the owner (plays Trayloom)

    private static int Owner(string exe, string go, string info)
    {
        var self = Process.GetCurrentProcess();
        File.WriteAllText(info, $"{self.Id} {self.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture)}");
        while (!File.Exists(go))
        {
            Thread.Sleep(20);
        }

        // exactly what WindowGuardianClient does
        var start = new ProcessStartInfo(exe, $"--window-guardian-launch {self.Id} {self.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture)}") { UseShellExecute = false, CreateNoWindow = true };
        using var launcher = Process.Start(start)!;
        launcher.WaitForExit(5000);
        File.WriteAllText(go + ".guardian", launcher.ExitCode.ToString(CultureInfo.InvariantCulture));
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    // ---------------------------------------------------------------- the windows

    private static int Windows(string mode, uint ownerPid, long ownerStart, string ready, string status)
    {
        _status = status;
        SetProcessDpiAwarenessContext(-4);
        Register(MainClass);
        Register(PopupClass);
        var monitors = WindowPlacementMath.GetMonitors();
        _good = WindowPlacementMath.Centered(monitors);
        var work = monitors[0].Work;

        _helper = CreateWindowExW(0, MainClass, "WinUI Desktop", 0x00CF0000, 0, 0, 100, 100, 0, 0, 0, 0); // the invisible helper every launch creates
        _main = CreateWindowExW(0, MainClass, "Dummy Music", 0x00CF0000, _good.Left, _good.Top, _good.Width, _good.Height, 0, 0, 0, 0);
        _popup = CreateWindowExW(0, PopupClass, "", 0x80000000, 10, 10, 100, 100, 0, 0, 0, 0);
        var originalEx = (long)GetWindowLongPtrW(_main, -20);

        // a popup left transparent, without any record (found by the heuristic pass)
        Ghost(_popup, false);

        var records = new List<string>();
        switch (mode)
        {
            case "kept":
                ShowWindow(_main, 5);
                ShowWindow(_main, 0);
                records.Add(Record(_main, "kept", null, _good));
                break;
            case "ghost":
                ShowWindow(_main, 7);
                Ghost(_main, true);
                records.Add(Record(_main, "ghost", originalEx, null));
                break;
            case "parked":
                ShowWindow(_main, 4);
                Ghost(_main, true);
                SetWindowPos(_main, 0, -30000, -30000, 2400, 1500, 0x4 | 0x10);
                records.Add(Record(_main, "parked", null, _good));
                records.Add(Record(_main, "ghost", originalEx, null));
                break;
            case "nearoff":
                ShowWindow(_main, 4);
                SetWindowPos(_main, 0, _good.Left, work.Bottom - 10, 1200, 800, 0x4 | 0x10);
                records.Add(Record(_main, "kept", null, _good));
                break;
            case "iconicoff":
                ShowWindow(_main, 7);
                var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                GetWindowPlacement(_main, ref wp);
                wp.rcNormalPosition = new RECT { Left = -30000, Top = -30000, Right = -27600, Bottom = -28500 };
                wp.showCmd = 7;
                SetWindowPlacement(_main, ref wp);
                ShowWindow(_main, 0);
                records.Add(Record(_main, "kept", null, _good));
                break;
            case "hiddenNoRecord":
                ShowWindow(_main, 5);
                ShowWindow(_main, 0);
                break;
            case "helperRecord":
                // records that name the untitled helper (an older build tracked it as parked): it is not a window of the person's and stays hidden
                ShowWindow(_main, 5);
                records.Add(Record(_helper, "parked", null, _good));
                records.Add(Record(_helper, "kept", null, _good));
                break;
            case "maximised":
                // the person's maximised window, hidden by the keeper: comes back minimised and still maximised when restored
                ShowWindow(_main, 3);
                ShowWindow(_main, 0);
                GetWindowRect(_main, out var zoomedRect);
                records.Add(Record(_main, "kept", null, zoomedRect));
                break;
            case "maximisedVisible":
                // a maximised window on screen with a release record (the release's verification failed once): it is not moved or resized
                ShowWindow(_main, 3);
                GetWindowRect(_main, out var onScreen);
                records.Add(Record(_main, "releasing", null, onScreen));
                break;
            default:
                return 2;
        }

        if (records.Count > 0)
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = $"{{\"v\":1,\"ownerPid\":{ownerPid},\"ownerStart\":{ownerStart.ToString(CultureInfo.InvariantCulture)},\"updated\":\"{DateTime.UtcNow:O}\",\"windows\":[{string.Join(",", records)}]}}";
            File.WriteAllText(WindowGuardianState.FilePath, json, new UTF8Encoding(false));
        }
        else if (File.Exists(WindowGuardianState.FilePath))
        {
            File.Delete(WindowGuardianState.FilePath);
        }

        Sample();
        File.WriteAllText(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        SetTimer(_main, 1, 25, 0);
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            if (msg.message == 0x0113 || msg.message == 0)
            {
                Sample();
            }
        }

        return 0;
    }

    private static string Record(nint hwnd, string kind, long? exStyle, RECT? home)
    {
        var pid = Environment.ProcessId;
        var ex = exStyle is { } e ? e.ToString(CultureInfo.InvariantCulture) : "null";
        var h = home is { } r ? $"[{r.Left},{r.Top},{r.Right},{r.Bottom}]" : "null";
        return $"{{\"hwnd\":{(long)hwnd},\"pid\":{pid},\"process\":\"AmDummy\",\"cls\":\"{MainClass}\",\"kind\":\"{kind}\",\"exStyle\":{ex},\"home\":{h}}}";
    }

    private static void Ghost(nint hwnd, bool tool)
    {
        var ex = (long)GetWindowLongPtrW(hwnd, -20);
        SetWindowLongPtrW(hwnd, -20, (nint)(ex | ExLayered | ExTransparent | (tool ? ExToolWindow : 0)));
        SetLayeredWindowAttributes(hwnd, 0, 0, 2);
    }

    private static bool IsGhost(nint hwnd)
    {
        var ex = (long)GetWindowLongPtrW(hwnd, -20);
        return (ex & ExLayered) != 0 && (ex & ExTransparent) != 0 && GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & 2) != 0 && alpha == 0;
    }

    /// <summary>Logs the state of the windows (one line per change): good = visible, opaque, no tool window style, minimised or comfortably on screen.</summary>
    private static void Sample()
    {
        if (_rehide is not null && File.Exists(_rehide))
        {
            File.Delete(_rehide);
            ShowWindow(_main, 0);
            var self = Process.GetCurrentProcess();
            var json = $"{{\"v\":1,\"ownerPid\":{Environment.ProcessId},\"ownerStart\":{self.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture)},\"updated\":\"{DateTime.UtcNow:O}\",\"windows\":[{Record(_main, "kept", null, _good)}]}}";
            File.WriteAllText(WindowGuardianState.FilePath, json, new UTF8Encoding(false));
        }

        var monitors = WindowPlacementMath.GetMonitors();
        var visible = IsWindowVisible(_main);
        var iconic = IsIconic(_main);
        var ex = (long)GetWindowLongPtrW(_main, -20);
        GetWindowRect(_main, out var rect);
        var place = rect;
        if (iconic)
        {
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(_main, ref wp))
            {
                place = (ex & ExToolWindow) != 0 ? wp.rcNormalPosition : WindowPlacementMath.WorkspaceToScreen(wp.rcNormalPosition, monitors);
            }
        }

        var comfortable = WindowPlacementMath.IsComfortablyVisible(place, monitors);
        var ghost = (ex & (ExLayered | ExTransparent | ExToolWindow)) != 0;
        var popupGhost = IsGhost(_popup);
        var good = visible && !ghost && comfortable && !popupGhost;
        var helperShown = IsWindowVisible(_helper);
        var zoomedPlacement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(_main, ref zoomedPlacement);

        // maximised now, or minimised and set to come back maximised
        var zoomed = IsZoomed(_main) || (zoomedPlacement.flags & 2) != 0;
        var line = $"good={(good ? 1 : 0)} helperShown={(helperShown ? 1 : 0)} zoomed={(zoomed ? 1 : 0)} visible={(visible ? 1 : 0)} iconic={(iconic ? 1 : 0)} exBits={(ghost ? 1 : 0)} popupGhost={(popupGhost ? 1 : 0)} comfortable={(comfortable ? 1 : 0)} rect=[{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}] place=[{place.Left},{place.Top},{place.Right},{place.Bottom}] ex=0x{ex:X}";
        if (line != _lastLine)
        {
            _lastLine = line;
            File.AppendAllText(_status, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
        }
    }

    private static void Register(string name)
    {
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            lpszClassName = name,
            hInstance = GetModuleHandleW(null),
        };
        if (RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW " + name + " failed: " + Marshal.GetLastWin32Error());
        }
    }

    // ---------------------------------------------------------------- Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint hwnd, nuint id, uint elapse, nint proc);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }
}
