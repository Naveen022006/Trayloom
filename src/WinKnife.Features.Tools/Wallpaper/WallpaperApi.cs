// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinKnife.Core.Native;

namespace WinKnife.Features.Tools;

internal enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RECT rect);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(WallpaperFit position);

    WallpaperFit GetPosition();
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass
{
}

/// <summary>Sets the desktop picture through IDesktopWallpaper (fill = crop) with the SystemParametersInfo fallback.</summary>
internal static class WallpaperApi
{
    private const uint SpiSetDeskWallpaper = 0x0014;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, string? vparam, uint winIni);

    /// <summary>Device paths of the monitors with the physical rectangle of each.</summary>
    public static IReadOnlyList<(string Id, RECT Rect)> Monitors()
    {
        var list = new List<(string, RECT)>();
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            var count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var id = wallpaper.GetMonitorDevicePathAt(i);
                if (!string.IsNullOrEmpty(id))
                {
                    wallpaper.GetMonitorRECT(id, out var rect);
                    list.Add((id, rect));
                }
            }
        }
        catch
        {
            // COM interface unavailable
        }

        return list;
    }

    /// <summary>The monitor id under the physical point (null when unknown).</summary>
    public static string? MonitorAt(int x, int y)
    {
        foreach (var (id, rect) in Monitors())
        {
            if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
            {
                return id;
            }
        }

        return Monitors().Select(m => m.Id).FirstOrDefault();
    }

    /// <summary>The path currently shown on a monitor (null = first), or null.</summary>
    public static string? Current(string? monitorId)
    {
        try
        {
            return ((IDesktopWallpaper)new DesktopWallpaperClass()).GetWallpaper(monitorId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Applies <paramref name="path"/> with the Fill position to one monitor, or to all monitors when the id is null.</summary>
    public static void Apply(string path, string? monitorId)
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.SetPosition(WallpaperFit.Fill);
            wallpaper.SetWallpaper(monitorId, path);
            return;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            // fall back below
        }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            key?.SetValue("WallpaperStyle", "10");
            key?.SetValue("TileWallpaper", "0");
        }

        if (!SystemParametersInfoW(SpiSetDeskWallpaper, 0, path, 0x01 | 0x02))
        {
            throw new InvalidOperationException("Windows refused to change the wallpaper.");
        }
    }
}
