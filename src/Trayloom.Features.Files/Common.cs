// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.Files;

/// <summary>Foreground / placement helpers shared by the floating windows of this project.</summary>
internal static class WinUtil
{
    /// <summary>Restores a minimized window and brings it to the foreground.</summary>
    public static void Activate(nint hwnd)
    {
        if (hwnd == 0 || !WinApi.IsWindow(hwnd))
        {
            return;
        }

        if (WinApi.IsIconic(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        ForceForeground(hwnd);
    }

    /// <summary>SetForegroundWindow with the AttachThreadInput workaround for the foreground lock.</summary>
    public static bool ForceForeground(nint hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && WinApi.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            WinApi.BringWindowToTop(hwnd);
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                WinApi.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// Creates the window handle, moves the window onto the monitor containing the physical point (so WPF picks that
    /// monitor's DPI) and returns the resulting scale factor. Call before <c>Show()</c>.
    /// </summary>
    public static double PrepareOnMonitorAt(Window window, int x, int y)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // Move only: a placeholder size here would leave the window ~100 px wide after Show().
        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return NativeMethods.GetDpiForWindow(hwnd) / 96.0;
    }

    /// <summary>Desired size of a not-yet-shown window's content in device-independent units (fixed Width / Height respected).</summary>
    public static Size DesiredSize(Window window)
    {
        if (window.Content is not FrameworkElement content)
        {
            return new Size(window.Width, window.Height);
        }

        var availableWidth = double.IsNaN(window.Width) ? double.PositiveInfinity : window.Width;
        var availableHeight = double.IsNaN(window.Height) ? double.PositiveInfinity : window.Height;
        content.Measure(new Size(availableWidth, availableHeight));
        var width = double.IsNaN(window.Width) ? content.DesiredSize.Width : window.Width;
        var height = double.IsNaN(window.Height) ? content.DesiredSize.Height : window.Height;
        return new Size(width, height);
    }

    /// <summary>Moves a window (keeping its size) to physical pixel coordinates, topmost, without activating it.</summary>
    public static void MoveTo(Window window, int x, int y, bool topmost = true)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, topmost ? HWND_TOPMOST : 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | (topmost ? 0u : SWP_NOZORDER));
    }

    /// <summary>Asks Windows to round the corners of a borderless window.</summary>
    public static void RoundCorners(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var value = DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Windows 10: square corners.
        }
    }

    /// <summary>Hides a window from Alt+Tab / the taskbar (adds WS_EX_TOOLWINDOW), optionally never activating on click.</summary>
    public static void MakeToolWindow(Window window, bool noActivate = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW;
        if (noActivate)
        {
            style |= WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
    }

    /// <summary>True when any of Ctrl, Alt, Shift or Win is down.</summary>
    public static bool AnyModifierDown() =>
        Down(VK_MENU) || Down(VK_CONTROL) || Down(VK_SHIFT) || Down(VK_LWIN) || Down(VK_RWIN);

    public static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The window handle that currently has the foreground when it is not one of this process' windows (else 0).</summary>
    public static nint ForeignForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        return pid == (uint)Environment.ProcessId ? 0 : foreground;
    }

    /// <summary>Lower-cased executable name (<c>chrome.exe</c>) of the process owning the window, or an empty string.</summary>
    public static string ProcessImageName(nint hwnd)
    {
        var path = WinApi.GetWindowProcessPath(hwnd, out _);
        return string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path).ToLowerInvariant();
    }
}

internal static class ModifierHelper
{
    /// <summary>
    /// Waits (up to <paramref name="timeoutMs"/>) until the modifier keys of the shortcut that triggered an action
    /// are released, then force-releases anything still held so a synthetic Ctrl+V is not turned into Ctrl+Alt+V.
    /// </summary>
    public static async Task ReleaseAsync(int timeoutMs = 1200)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (WinUtil.AnyModifierDown() && timer.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(15);
        }

        foreach (var vk in new[] { VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN })
        {
            if (WinUtil.Down(vk))
            {
                InputSender.SendKey((ushort)vk, keyUp: true);
            }
        }
    }
}

/// <summary>Icons of files, folders and executables (shell icons), cached per key.</summary>
internal static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shell icon of an existing path (large 32 px) or, when it does not exist, of its extension.</summary>
    public static ImageSource? ForPath(string path, bool small = false)
    {
        var exists = File.Exists(path) || Directory.Exists(path);
        var ext = Path.GetExtension(path);
        var key = (exists && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)))
            ? path
            : exists && Directory.Exists(path) ? "<dir>" : "ext:" + ext;
        key += small ? "|s" : "|l";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            var info = new SHFILEINFO();
            var isDir = Directory.Exists(path);
            var flags = WinApi.SHGFI_ICON | (small ? WinApi.SHGFI_SMALLICON : WinApi.SHGFI_LARGEICON);
            var attrs = isDir ? WinApi.FILE_ATTRIBUTE_DIRECTORY : WinApi.FILE_ATTRIBUTE_NORMAL;
            if (!exists || !(ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)))
            {
                flags |= WinApi.SHGFI_USEFILEATTRIBUTES;
            }

            var lookup = exists && (flags & WinApi.SHGFI_USEFILEATTRIBUTES) == 0 ? path : (isDir ? "folder" : "file" + (string.IsNullOrEmpty(ext) ? ".bin" : ext));
            if (WinApi.SHGetFileInfoW(lookup, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) != 0 && info.hIcon != 0)
            {
                try
                {
                    image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                }
                finally
                {
                    WinApi.DestroyIcon(info.hIcon);
                }
            }
        }
        catch (Exception)
        {
            image = null;
        }

        Cache[key] = image;
        return image;
    }

    /// <summary>Icon of an executable image given as a full path (null when unknown).</summary>
    public static ImageSource? ForExecutable(string? exePath) =>
        string.IsNullOrEmpty(exePath) || !File.Exists(exePath) ? null : ForPath(exePath, small: true);
}

/// <summary>
/// Tracks which executables came to the foreground recently (the clipboard names no source for a copy, so a copy is judged
/// against every app that was in front since the last look). Must be created on a thread with a message loop (the UI thread).
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly WinApi.WinEventProc _callback;
    private readonly List<(DateTime At, string Image, string? Path)> _recent = [];
    private nint _hook;

    public ForegroundTracker()
    {
        _callback = OnEvent;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_SYSTEM_FOREGROUND, WinApi.EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT);
        Remember(NativeMethods.GetForegroundWindow());
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }
    }

    /// <summary>Executable names that were in front during the last <paramref name="window"/> (plus the current one).</summary>
    public IReadOnlyList<(string Image, string? Path)> Recent(TimeSpan window)
    {
        Remember(NativeMethods.GetForegroundWindow());
        var cutoff = DateTime.UtcNow - window;
        return [.. _recent.Where(r => r.At >= cutoff).Select(r => (r.Image, r.Path))];
    }

    /// <summary>Raised (UI thread) after the foreground window changed.</summary>
    public event Action<nint>? ForegroundChanged;

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        Remember(hwnd);
        ForegroundChanged?.Invoke(hwnd);
    }

    private void Remember(nint hwnd)
    {
        try
        {
            var path = WinApi.GetWindowProcessPath(hwnd, out var pid);
            if (string.IsNullOrEmpty(path) || pid == (uint)Environment.ProcessId)
            {
                return;
            }

            var image = Path.GetFileName(path).ToLowerInvariant();
            _recent.RemoveAll(r => r.At < DateTime.UtcNow.AddSeconds(-30));
            _recent.Add((DateTime.UtcNow, image, path));
        }
        catch (Exception)
        {
            // best effort
        }
    }
}

/// <summary>Services shared between the feature instances and the (stateless) contribution classes of this project.</summary>
internal static class Hub
{
    /// <summary>The running clipboard history feature, null while it is not installed.</summary>
    public static IClipboardHost? Clipboard { get; set; }

    /// <summary>The running shelf feature, null while it is not installed.</summary>
    public static IShelfHost? Shelf { get; set; }

    /// <summary>The running URL cleaner, null while it is not installed.</summary>
    public static ICleanUrlHost? CleanUrl { get; set; }

    /// <summary>The flat app settings (set by the features and panel sections; used where a context is not at hand).</summary>
    public static IAppSettings? Settings { get; set; }
}

internal static class Lists
{
    /// <summary>Normalises an app identity (process image name or path) for comparisons.</summary>
    public static string Key(string identity) => identity.Trim().ToLowerInvariant();

    /// <summary>True when <paramref name="imageName"/> (or its full path) matches an entry of the list.</summary>
    public static bool Contains(IEnumerable<string> list, string? imageName, string? fullPath = null)
    {
        foreach (var raw in list)
        {
            var entry = Key(raw);
            if (entry.Length == 0)
            {
                continue;
            }

            if (imageName is not null && (entry == imageName.ToLowerInvariant() || Path.GetFileName(entry) == imageName.ToLowerInvariant()))
            {
                return true;
            }

            if (fullPath is not null && string.Equals(entry, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
