// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>Resolves a process id to a stable key, a friendly name and an icon (cached per executable).</summary>
internal static class AppIdentity
{
    private static readonly Dictionary<string, (string Name, ImageSource? Icon)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string SystemSoundsKey => "system sounds";

    public static (string Key, string Name, ImageSource? Icon) Resolve(uint processId, bool isSystemSounds)
    {
        if (isSystemSounds || processId == 0)
        {
            return (SystemSoundsKey, Loc.T("System sounds"), null);
        }

        var path = PathOf(processId);
        if (path is null)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return ($"{process.ProcessName.ToLowerInvariant()}.exe", process.ProcessName, null);
            }
            catch (Exception)
            {
                return ($"pid:{processId}", Loc.F("Process {0}", processId), null);
            }
        }

        var key = Path.GetFileName(path).ToLowerInvariant();
        if (!Cache.TryGetValue(path, out var info))
        {
            info = (Describe(path), LoadIcon(path));
            Cache[path] = info;
        }

        return (key, info.Name, info.Icon);
    }

    /// <summary>The same key as <see cref="Resolve"/> without loading the name or icon (cheap, any thread); null when the process is gone.</summary>
    public static string? KeyOf(uint processId, bool isSystemSounds)
    {
        if (isSystemSounds || processId == 0)
        {
            return SystemSoundsKey;
        }

        var path = PathOf(processId);
        if (path is not null)
        {
            return Path.GetFileName(path).ToLowerInvariant();
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName.ToLowerInvariant()}.exe";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Describe(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception)
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? PathOf(uint processId)
    {
        var handle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(1024);
            var size = builder.Capacity;
            return QueryFullProcessImageNameW(handle, 0, builder, ref size) ? builder.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
