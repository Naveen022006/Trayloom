// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core.Native;

namespace Trayloom.Features.Files;

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProviderCom
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
}

[ComImport, Guid("000214e2-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowserCom
{
    void GetWindow(out nint hwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);

    void InsertMenusSB();

    void SetMenuSB();

    void RemoveMenusSB();

    void SetStatusTextSB();

    void EnableModelessSB();

    void TranslateAcceleratorSB();

    void BrowseObject();

    void GetViewStateStream();

    void GetControlWindow();

    void SendControlMsg();

    [PreserveSig]
    int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellViewCom view);
}

[ComImport, Guid("000214e3-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellViewCom
{
    void GetWindow(out nint hwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
}

/// <summary>File Explorer helpers: which window is Explorer, is a file list focused, the current folder and the selection.</summary>
internal static class ExplorerShell
{
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>True for an Explorer folder window, the desktop, or a common file dialog (the places a file list can have focus).</summary>
    public static bool IsExplorerWindow(nint hwnd, bool includeDialogs = true)
    {
        if (hwnd == 0)
        {
            return false;
        }

        var cls = WinApi.GetClass(hwnd);
        switch (cls)
        {
            case "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW":
                return ProcessIsExplorer(hwnd);
            case "#32770" when includeDialogs:
                return WinApi.FindWindowExW(hwnd, 0, "DUIViewWndClassName", null) != 0 || WinApi.FindWindowExW(hwnd, 0, "SHELLDLL_DefView", null) != 0;
            default:
                return false;
        }
    }

    private static bool ProcessIsExplorer(nint hwnd)
    {
        var path = WinApi.GetWindowProcessPath(hwnd, out _);
        return path is not null && Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when keyboard focus of the foreground window sits in a file list (not in a rename box, address bar or search box).
    /// Cheap enough for a keyboard hook (a couple of Win32 calls).
    /// </summary>
    public static bool FileListFocused(nint foreground)
    {
        if (foreground == 0)
        {
            return false;
        }

        var thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!WinApi.GetGUIThreadInfo(thread, ref info))
        {
            return false;
        }

        var focus = info.hwndFocus;
        if (focus == 0)
        {
            return false;
        }

        var cls = WinApi.GetClass(focus);
        return cls is "DirectUIHWND" or "SysListView32" or "SHELLDLL_DefView" or "SysTreeView32" or "NamespaceTreeControl" or "UIItemsView";
    }

    /// <summary>The folder shown by the active tab of the Explorer window <paramref name="top"/> (null when unknown, e.g. "This PC").</summary>
    public static string? CurrentFolder(nint top)
    {
        var cls = WinApi.GetClass(top);
        if (cls is "Progman" or "WorkerW")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        var path = ActiveWindow(top) is { } window ? SafePath(window) : null;
        return path;
    }

    /// <summary>The selected items (file system paths) of the Explorer window <paramref name="top"/>.</summary>
    public static List<string> Selection(nint top)
    {
        var result = new List<string>();
        try
        {
            dynamic? window = ActiveWindow(top);
            if (window is null)
            {
                return result;
            }

            dynamic items = window.Document.SelectedItems();
            foreach (dynamic item in items)
            {
                string path = item.Path;
                if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                {
                    result.Add(path);
                }
            }
        }
        catch (Exception)
        {
            // window closed, no selection support
        }

        return result;
    }

    private static string? SafePath(dynamic window)
    {
        try
        {
            string path = window.Document.Folder.Self.Path;
            return Directory.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The shell window object of the active tab belonging to a top-level HWND (a dynamic COM object) or null.</summary>
    private static object? ActiveWindow(nint top)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic windows = shell.Windows();
            object? first = null;
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                object? item;
                try
                {
                    item = windows.Item(i);
                }
                catch (Exception)
                {
                    continue;
                }

                if (item is null)
                {
                    continue;
                }

                try
                {
                    dynamic window = item;
                    if ((long)window.HWND != (long)top)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                first ??= item;
                if (IsActiveTab(item))
                {
                    return item;
                }
            }

            return first;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>With tabbed Explorer several shell windows share one HWND; the active tab's shell view is the visible one.</summary>
    private static bool IsActiveTab(object window)
    {
        try
        {
            if (window is not IServiceProviderCom provider)
            {
                return true;
            }

            var service = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            if (provider.QueryService(ref service, ref iid, out var browserObject) != 0 || browserObject is not IShellBrowserCom browser)
            {
                return true;
            }

            if (browser.QueryActiveShellView(out var view) != 0 || view is null)
            {
                return true;
            }

            view.GetWindow(out var hwnd);
            return hwnd != 0 && WinApi.IsWindowVisible(hwnd);
        }
        catch (Exception)
        {
            return true;
        }
    }
}
