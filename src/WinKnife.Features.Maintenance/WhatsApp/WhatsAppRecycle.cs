// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinKnife.Features.Maintenance;

/// <summary>
/// Recycle Bin handling with a receipt. The shell's own delete would remove a file for good when the bin is disabled or the
/// file is larger than the bin, so a move is refused up front in those cases; after a successful move the file's
/// <c>$R</c> entry is located through its <c>$I</c> record so the organizer can restore it exactly (undo).
/// </summary>
internal static class WaRecycle
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder volumeName, uint length);

    /// <summary>A moved item: where its content now lives in the bin and where it came from.</summary>
    public sealed record Receipt(string BinPath, string OriginalPath);

    /// <summary>True when sending <paramref name="path"/> to the Recycle Bin will really keep it recoverable.</summary>
    public static bool CanRecycle(string path, long size)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                return false;
            }

            long? maxBytes = null;
            var volumeName = new StringBuilder(64);
            if (GetVolumeNameForVolumeMountPointW(root, volumeName, (uint)volumeName.Capacity))
            {
                var guid = volumeName.ToString();
                var open = guid.IndexOf('{');
                var close = guid.IndexOf('}');
                if (open >= 0 && close > open)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid[open..(close + 1)]);
                    if (key is not null)
                    {
                        if (key.GetValue("NukeOnDelete") is int nuke && nuke != 0)
                        {
                            return false;
                        }

                        if (key.GetValue("MaxCapacity") is int megabytes && megabytes > 0)
                        {
                            maxBytes = (long)megabytes * 1024 * 1024;
                        }
                    }
                }
            }

            maxBytes ??= drive.TotalSize / 20;
            return size <= maxBytes;
        }
        catch
        {
            return false;
        }
    }

    private static string? BinFolder(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            return string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sid) ? null : Path.Combine(root, "$Recycle.Bin", sid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sends a file to the Recycle Bin (after the safety checks) and returns where it went, or null on failure.</summary>
    public static Receipt? Trash(string path, long size)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) || !FileOps.MayRemove(full) || Win32.IsReparsePoint(full) || !CanRecycle(full, size))
        {
            return null;
        }

        var before = DateTime.UtcNow.AddSeconds(-2);
        if (!FileOps.Recycle(full))
        {
            return null;
        }

        return FindReceipt(full, before) ?? new Receipt(string.Empty, full);
    }

    /// <summary>Locates the bin entry of a just-recycled path through its $I record; null when it cannot be found.</summary>
    public static Receipt? FindReceipt(string originalPath, DateTime notBeforeUtc)
    {
        try
        {
            var folder = BinFolder(originalPath);
            if (folder is null || !Directory.Exists(folder))
            {
                return null;
            }

            Receipt? best = null;
            var bestTime = DateTime.MinValue;
            foreach (var info in Directory.EnumerateFiles(folder, "$I*"))
            {
                var record = ReadRecord(info);
                if (record is null || !string.Equals(record.Value.Path, originalPath, StringComparison.OrdinalIgnoreCase) || record.Value.DeletedUtc < notBeforeUtc || record.Value.DeletedUtc < bestTime)
                {
                    continue;
                }

                var content = Path.Combine(folder, "$R" + Path.GetFileName(info)[2..]);
                if (File.Exists(content) || Directory.Exists(content))
                {
                    best = new Receipt(content, record.Value.Path);
                    bestTime = record.Value.DeletedUtc;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static (string Path, DateTime DeletedUtc, long Size)? ReadRecord(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length < 28)
            {
                return null;
            }

            var version = BitConverter.ToInt64(bytes, 0);
            var size = BitConverter.ToInt64(bytes, 8);
            var deleted = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 16));
            string text;
            if (version == 2)
            {
                var chars = BitConverter.ToInt32(bytes, 24);
                if (chars <= 0 || 28 + (chars * 2) > bytes.Length)
                {
                    return null;
                }

                text = Encoding.Unicode.GetString(bytes, 28, chars * 2);
            }
            else if (version == 1)
            {
                text = Encoding.Unicode.GetString(bytes, 24, bytes.Length - 24);
            }
            else
            {
                return null;
            }

            return (text.TrimEnd('\0'), deleted, size);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Puts a recycled file back at <paramref name="restorePath"/> (never overwrites) and drops its $I record.</summary>
    public static bool Restore(string binPath, string restorePath)
    {
        try
        {
            if (!IsBinContent(binPath) || !File.Exists(binPath) || File.Exists(restorePath) || Directory.Exists(restorePath))
            {
                return false;
            }

            var folder = Path.GetDirectoryName(restorePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Move(binPath, restorePath);
            try
            {
                File.Delete(Path.Combine(Path.GetDirectoryName(binPath)!, "$I" + Path.GetFileName(binPath)[2..]));
            }
            catch
            {
                // an orphan record is harmless
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True for a $R entry inside a $Recycle.Bin folder.</summary>
    public static bool IsBinContent(string path) =>
        path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("$R", StringComparison.OrdinalIgnoreCase);
}
