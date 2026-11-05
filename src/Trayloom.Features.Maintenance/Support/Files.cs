// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

/// <summary>Number, size and time formatting that follows the macOS conventions the spec quotes.</summary>
internal static class Fmt
{
    /// <summary>File-style byte count (1000 based): "Zero KB", "512 bytes", "4 KB", "65.4 MB".</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "Zero KB";
        }

        if (bytes < 1000)
        {
            return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        }

        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        var text = unit == 0
            ? Math.Round(value).ToString("0", CultureInfo.CurrentCulture)
            : value.ToString(value < 10 || unit >= 2 ? "0.#" : "0.#", CultureInfo.CurrentCulture);
        if (unit >= 2 && value >= 100)
        {
            text = Math.Round(value).ToString("0", CultureInfo.CurrentCulture);
        }

        return $"{text} {units[unit]}";
    }

    /// <summary>1024-based metric (Kill Process memory column): one decimal below 10, none above.</summary>
    public static string MetricBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (unit < units.Length - 1 && Math.Round(value, value < 10 ? 1 : 0) >= 1024)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            return $"{(long)value} B";
        }

        return value < 10
            ? $"{value.ToString("0.0", CultureInfo.CurrentCulture)} {units[unit]}"
            : $"{Math.Round(value).ToString("0", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    /// <summary>"Ns", "Nmin", "Nh Mmin".</summary>
    public static string Elapsed(TimeSpan span)
    {
        var seconds = (int)Math.Max(0, span.TotalSeconds);
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return $"{minutes}min";
        }

        return $"{minutes / 60}h {minutes % 60}min";
    }

    /// <summary>Named relative time: "now", "5 minutes ago", "yesterday", "3 days ago".</summary>
    public static string Ago(DateTime then)
    {
        var span = DateTime.Now - then;
        if (span.TotalSeconds < 60)
        {
            return "now";
        }

        if (span.TotalMinutes < 60)
        {
            var m = (int)span.TotalMinutes;
            return m == 1 ? "1 minute ago" : $"{m} minutes ago";
        }

        if (span.TotalHours < 24)
        {
            var h = (int)span.TotalHours;
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }

        var days = (int)span.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    /// <summary>"Today, 9:00 AM" / "Tomorrow, 9:00 AM" / medium date + short time.</summary>
    public static string DayTime(DateTime when)
    {
        var culture = CultureInfo.CurrentCulture;
        var time = when.ToString("t", culture);
        var diff = (when.Date - DateTime.Today).Days;
        return diff switch
        {
            0 => $"Today, {time}",
            1 => $"Tomorrow, {time}",
            -1 => $"Yesterday, {time}",
            _ => $"{when.ToString("d", culture)}, {time}",
        };
    }

    /// <summary>Short date + short time ("10/31/26, 9:00 AM").</summary>
    public static string ShortDateTime(DateTime when) =>
        $"{when.ToString("d", CultureInfo.CurrentCulture)}, {when.ToString("t", CultureInfo.CurrentCulture)}";

    /// <summary>The user profile replaced by "~".</summary>
    public static string Home(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            && (path.Length == home.Length || path[home.Length] == '\\'))
        {
            return "~" + path[home.Length..];
        }

        return path;
    }

    /// <summary>Epoch seconds now.</summary>
    public static double Epoch() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Date from epoch seconds in local time.</summary>
    public static DateTime FromEpoch(double seconds) => DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime;
}

/// <summary>Result of a deletion.</summary>
internal readonly record struct DeleteResult(long Freed, bool Failed);

/// <summary>Outcome of <see cref="FileOps.TryRecycle"/>.</summary>
internal enum RecycleResult
{
    /// <summary>Moved to the Recycle Bin.</summary>
    Recycled,

    /// <summary>The path did not exist.</summary>
    Missing,

    /// <summary>Not moved: the bin would delete it for good (disabled, too large, removable or network drive).</summary>
    NotRecyclable,

    /// <summary>The shell operation failed.</summary>
    Failed,
}

/// <summary>File-system helpers: recycle bin, permanent delete, sizes, identity, reveal.</summary>
internal static class FileOps
{
    private static readonly EnumerationOptions SizeOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Win32,
    };

    /// <summary>Environment folder helper.</summary>
    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;

    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public static string Temp => Path.GetTempPath().TrimEnd('\\');

    /// <summary>True when the current process is elevated.</summary>
    public static bool IsElevated => ElevationPolicy.IsElevated;

    /// <summary>Folder size (sum of file lengths, reparse points skipped); cancellable.</summary>
    public static long DirectorySize(string path, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SizeOptions))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    total += file.Length;
                }
                catch
                {
                    // vanished
                }
            }

            return total;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>True when the path or any parent up to (excluding) <paramref name="root"/> is a reparse point.</summary>
    public static bool HasReparseInChain(string path, string? root)
    {
        try
        {
            var current = Path.GetFullPath(path).TrimEnd('\\');
            var stop = root is null ? null : Path.GetFullPath(root).TrimEnd('\\');
            while (!string.IsNullOrEmpty(current))
            {
                if (stop is not null && string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if ((File.Exists(current) || Directory.Exists(current)) && Win32.IsReparsePoint(current))
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(current);
                if (parent is null || parent.Length == current.Length)
                {
                    break;
                }

                current = parent.TrimEnd('\\');
                if (current.Length <= 3)
                {
                    break;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>True when <paramref name="path"/> equals or lies under <paramref name="root"/>.</summary>
    public static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var p = path.TrimEnd('\\');
        var r = root.TrimEnd('\\');
        return p.Equals(r, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Number of path segments after the drive.</summary>
    public static int SegmentCount(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
            return Path.GetFullPath(path)[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Last line of defence (spec 7.1.7 <c>mayRemove</c>): never a drive root, a critical folder, a link, and always at
    /// least four segments deep.
    /// </summary>
    public static bool MayRemove(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (SegmentCount(full) < 3 || full.Length <= 3)
            {
                return false;
            }

            if (SafePaths.IsCriticalOrAncestor(full))
            {
                return false;
            }

            if (full.EndsWith(".localized", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !Win32.IsReparsePoint(full);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The folders that must never be removed as a whole (see <see cref="SafePaths.CriticalRoots"/>).</summary>
    private static IEnumerable<string> CriticalRoots() => SafePaths.CriticalRoots();

    /// <summary>True for a drive root, a critical folder, or a folder containing one.</summary>
    public static bool IsCriticalOrAncestor(string p) => SafePaths.IsCriticalOrAncestor(p);

    /// <summary>
    /// True when <paramref name="path"/> is a folder the Cleaner may treat as a temp root: not a link, not critical, at least three
    /// segments deep, named <c>Temp</c> or <c>Tmp</c> (or an all-digit RDP session folder directly below a <c>Temp</c>).
    /// </summary>
    internal static bool IsSafeTempRoot(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var full = SafePaths.Normalize(path);
            if (Win32.IsReparsePoint(full) || SafePaths.IsCriticalOrAncestor(full) || SafePaths.SegmentCount(full) < 3)
            {
                return false;
            }

            var leaf = Path.GetFileName(full);
            if (leaf.Equals("Temp", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Tmp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? string.Empty);
            return leaf.Length > 0 && leaf.All(char.IsAsciiDigit) && parent.Equals("Temp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary><see cref="Temp"/> when it passes <see cref="IsSafeTempRoot"/>, else null. Use this instead of <see cref="Temp"/> to scan or clean.</summary>
    public static string? SafeTemp
    {
        get
        {
            var temp = Temp;
            return IsSafeTempRoot(temp) ? temp : null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder volumeName, uint length);

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
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid[open..(close + 1)]);
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

    /// <summary>Moves a file or folder to the Recycle Bin. Returns false when it could not be moved or would not stay recoverable.</summary>
    public static bool Recycle(string path) => TryRecycle(path) is RecycleResult.Recycled or RecycleResult.Missing;

    /// <summary>
    /// Sends a path to the Recycle Bin only when that keeps it recoverable (<see cref="CanRecycle"/>); the shell would otherwise
    /// delete it for good. <paramref name="knownSize"/> saves a size walk.
    /// </summary>
    public static RecycleResult TryRecycle(string path, long? knownSize = null)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return RecycleResult.Missing;
            }

            var size = knownSize ?? DirectorySize(path);
            if (!CanRecycle(path, size))
            {
                return RecycleResult.NotRecyclable;
            }

            var op = new Win32.SHFILEOPSTRUCT
            {
                wFunc = Win32.FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_NOERRORUI | Win32.FOF_SILENT | Win32.FOF_WANTNUKEWARNING),
            };
            var result = Win32.SHFileOperationW(ref op);
            return result == 0 && !op.fAnyOperationsAborted && !File.Exists(path) && !Directory.Exists(path)
                ? RecycleResult.Recycled
                : RecycleResult.Failed;
        }
        catch
        {
            return RecycleResult.Failed;
        }
    }

    /// <summary>Deletes a file or folder permanently, removing whatever can be removed. Never follows links.</summary>
    public static DeleteResult DeletePermanently(string path, long? knownSize = null)
    {
        var failed = false;
        long freed = 0;
        try
        {
            if (File.Exists(path))
            {
                var length = new FileInfo(path).Length;
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    freed += length;
                }
                catch
                {
                    failed = true;
                }

                return new DeleteResult(freed, failed);
            }

            if (!Directory.Exists(path))
            {
                return new DeleteResult(0, false);
            }

            if (Win32.IsReparsePoint(path))
            {
                Directory.Delete(path);
                return new DeleteResult(0, false);
            }

            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                try
                {
                    var length = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += length;
                }
                catch
                {
                    failed = true;
                }
            }

            foreach (var sub in dir.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    try
                    {
                        sub.Delete();
                    }
                    catch
                    {
                        failed = true;
                    }

                    continue;
                }

                var child = DeletePermanently(sub.FullName);
                freed += child.Freed;
                failed |= child.Failed;
            }

            try
            {
                dir.Delete(false);
            }
            catch
            {
                failed = true;
            }
        }
        catch
        {
            failed = true;
        }

        return new DeleteResult(freed, failed);
    }

    /// <summary>Opens Explorer with the item selected.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
                }
            }
        }
        catch
        {
            // nothing to do
        }
    }

    /// <summary>Opens a folder in Explorer.</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch
        {
            // nothing to do
        }
    }

    /// <summary>Recycle Bin size and item count (all drives).</summary>
    public static (long Bytes, long Items) RecycleBinInfo()
    {
        var info = new Win32.SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<Win32.SHQUERYRBINFO>() };
        return Win32.SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    /// <summary>Empties the Recycle Bin without prompts.</summary>
    public static bool EmptyRecycleBin() =>
        Win32.SHEmptyRecycleBinW(0, null, Win32.SHERB_NOCONFIRMATION | Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND) is 0 or unchecked((int)0x8000FFFF) or 1;

    /// <summary>Replaces invalid file-name characters, trims dots and spaces, avoids reserved names.</summary>
    public static string SanitizeName(string name, int maxLength = 200)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(char.IsControl(ch) || "<>:\"/\\|?*".Contains(ch) ? '-' : ch);
        }

        var text = builder.ToString().Trim().Trim('.', '-', ' ');
        if (text.Length == 0)
        {
            return Loc.T("Output");
        }

        var upper = text.ToUpperInvariant();
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(upper))
        {
            text += "-";
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }
}

/// <summary>Shell icons (SHGetFileInfo) as frozen bitmap sources, cached per path.</summary>
internal static class Icons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon of a path (file, folder or an exe inside "path,index"). Falls back to the generic file icon.</summary>
    public static ImageSource? Of(string? path, bool large = false)
    {
        var key = (large ? "L|" : "S|") + (path ?? string.Empty);
        return Cache.GetOrAdd(key, _ => Load(path, large));
    }

    private static ImageSource? Load(string? path, bool large)
    {
        try
        {
            var clean = CleanIconPath(path);
            var info = new Win32.SHFILEINFO();
            var flags = Win32.SHGFI_ICON | (large ? Win32.SHGFI_LARGEICON : Win32.SHGFI_SMALLICON);
            var target = clean;
            if (string.IsNullOrEmpty(target) || !(File.Exists(target) || Directory.Exists(target)))
            {
                flags |= Win32.SHGFI_USEFILEATTRIBUTES;
                target = string.IsNullOrEmpty(target) ? ".exe" : (Path.GetExtension(target) is { Length: > 0 } ext ? ext : ".exe");
            }

            var result = Win32.SHGetFileInfoW(target, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), flags);
            if (result == 0 || info.hIcon == 0)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Win32.DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strips quotes and the ",index" suffix from a DisplayIcon value.</summary>
    public static string? CleanIconPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim().Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 1 && int.TryParse(text[(comma + 1)..].Trim(), out _))
        {
            text = text[..comma];
        }

        return Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
    }
}

/// <summary>
/// Runs short PowerShell scripts elevated (one UAC prompt) and reads the paths a removal reports back as failed. A removal
/// is guarded twice: C# refuses items that fail <see cref="IsAllowed"/>, and the script repeats the same rule (a PowerShell copy,
/// kept in step by a harness) on a manifest whose SHA-256 is fixed inside the encoded command, so a file swapped after the UAC
/// prompt is never acted on.
/// </summary>
internal static class Elevated
{
    private static readonly System.Text.RegularExpressions.RegexOptions s_options =
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;

    private static readonly System.Text.RegularExpressions.Regex s_regKey = new(
        @"^Registry::HKEY_(LOCAL_MACHINE|CURRENT_USER)\\SOFTWARE\\(WOW6432Node\\)?(?<seg>[^\\]+)(\\.*)?$", s_options);

    private static readonly System.Text.RegularExpressions.Regex s_runKey = new(
        @"^Registry::HKEY_(LOCAL_MACHINE|CURRENT_USER)\\SOFTWARE\\(WOW6432Node\\)?Microsoft\\Windows\\CurrentVersion\\Run(Once)?$", s_options);

    private static readonly System.Text.RegularExpressions.Regex s_serviceName = new(@"^[A-Za-z0-9_.\- ]{1,128}$");

    private static readonly string[] s_bannedSegments = ["Microsoft", "Classes", "Policies", "Wow6432Node", "RegisteredApplications"];

    private const string FirewallRules = @"Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    /// <summary>
    /// The PowerShell copy of <see cref="IsAllowed"/> plus the service check. The caller defines <c>$CriticalRoots</c> (array) and
    /// <c>$WinDir</c> first (see <see cref="ScriptPreamble"/>). <c>Test-Allowed</c> is pure (it only reads the item itself) so a harness can
    /// compare it with <see cref="IsAllowed"/>; <c>Test-ServiceSafe</c> reads the service registry key and is used by the removal script only.
    /// </summary>
    internal const string ValidationScript = """
        $RegKeyRe = '^Registry::HKEY_(LOCAL_MACHINE|CURRENT_USER)\\SOFTWARE\\(WOW6432Node\\)?(?<seg>[^\\]+)(\\.*)?$'
        $RunKeyRe = '^Registry::HKEY_(LOCAL_MACHINE|CURRENT_USER)\\SOFTWARE\\(WOW6432Node\\)?Microsoft\\Windows\\CurrentVersion\\Run(Once)?$'
        $FirewallKey = 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules'

        function Test-RegKeyPath([string]$path) {
          if (($path -split '\\') -contains '..' -or ($path -split '\\') -contains '.') { return $false }
          $m = [regex]::Match($path, $RegKeyRe, 'IgnoreCase')
          if (-not $m.Success) { return $false }
          $seg = $m.Groups['seg'].Value
          if (@('Microsoft', 'Classes', 'Policies', 'Wow6432Node', 'RegisteredApplications') -contains $seg) { return $false }
          return $true
        }

        function Test-Allowed([string]$kind, [string]$path, [string]$name) {
          try {
            if ([string]::IsNullOrEmpty($kind) -or [string]::IsNullOrEmpty($path)) { return $false }
            if ($path -match '[\x00-\x1f\x7f]' -or $name -match '[\x00-\x1f\x7f]') { return $false }
            if ($kind -ceq 'file') {
              if ($path -notmatch '^[A-Za-z]:\\') { return $false }
              $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
              $root = [IO.Path]::GetPathRoot($full)
              $segs = @($full.Substring($root.Length).Split('\') | Where-Object { $_ -ne '' })
              if ($segs.Count -lt 3) { return $false }
              $win = $WinDir.TrimEnd('\')
              if ($full -ieq $win -or $full.StartsWith($win + '\', [StringComparison]::OrdinalIgnoreCase)) { return $false }
              foreach ($c in $CriticalRoots) {
                $cc = ([string]$c).TrimEnd('\')
                if ($cc.Length -eq 0) { continue }
                if ($full -ieq $cc -or $cc.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase)) { return $false }
              }
              $it = Get-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
              if ($null -ne $it -and ($it.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
              return $true
            }
            if ($kind -ceq 'reg') { return (Test-RegKeyPath $path) }
            if ($kind -ceq 'regvalue') {
              if ([string]::IsNullOrEmpty($name)) { return $false }
              if (Test-RegKeyPath $path) { return $true }
              if ($path -match $RunKeyRe) { return $true }
              if ($path -ieq $FirewallKey) { return $true }
              return $false
            }
            if ($kind -ceq 'task') {
              if ($path.Length -lt 2 -or -not $path.StartsWith('\')) { return $false }
              if ($path.StartsWith('\Microsoft\', [StringComparison]::OrdinalIgnoreCase)) { return $false }
              if ($path.Contains('..') -or $path.Contains('"')) { return $false }
              return $true
            }
            if ($kind -ceq 'service') { return ($path -match '^[A-Za-z0-9_.\- ]{1,128}$') }
            return $false
          } catch { return $false }
        }

        function Test-ServiceSafe([string]$name) {
          try {
            $key = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $name) -ErrorAction Stop
            if ($null -eq $key) { return $false }
            if ($null -ne $key.Type -and (([int]$key.Type) -band 3) -ne 0) { return $false }
            if ($null -ne $key.Start -and ([int]$key.Start -eq 0 -or [int]$key.Start -eq 1)) { return $false }
            if ($null -eq $key.ImagePath) { return $false }
            $img = ([Environment]::ExpandEnvironmentVariables([string]$key.ImagePath)).Trim().TrimStart('"')
            if ($img.StartsWith($env:windir, [StringComparison]::OrdinalIgnoreCase)) { return $false }
            if ($img.StartsWith('\SystemRoot', [StringComparison]::OrdinalIgnoreCase)) { return $false }
            if ($img -match 'svchost') { return $false }
            return $true
          } catch { return $false }
        }
        """;

    /// <summary>
    /// The start of the removal script: refuses a link as the manifest folder (exit 4), reads the manifest bytes once into
    /// <c>$bytes</c> and exits with code 3 unless their SHA-256 equals <c>$expectedHash</c>. Needs <c>$manifestPath</c> and <c>$expectedHash</c>.
    /// </summary>
    internal const string HashPrologue = """
        function Test-DirSafe([string]$file) {
          $d = Get-Item -LiteralPath (Split-Path -Parent $file) -Force
          return (-not ($d.Attributes -band [IO.FileAttributes]::ReparsePoint))
        }
        if (-not (Test-DirSafe $manifestPath)) { exit 4 }
        $bytes = [IO.File]::ReadAllBytes($manifestPath)
        $sha = [Security.Cryptography.SHA256]::Create()
        $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
        if ($actual -ne $expectedHash) { exit 3 }
        """;

    /// <summary>One item of an elevated removal manifest.</summary>
    public sealed record Item(string Kind, string Path, string? Name = null, bool Recycle = false);

    /// <summary>
    /// The allow-list for elevated removals; the script repeats it. See the plan, section WP-0, for the rule per kind. Never throws.
    /// </summary>
    internal static bool IsAllowed(Item i)
    {
        try
        {
            var path = i.Path;
            if (string.IsNullOrEmpty(i.Kind) || string.IsNullOrEmpty(path) || HasControl(path) || HasControl(i.Name))
            {
                return false;
            }

            switch (i.Kind)
            {
                case "file":
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\"))
                    {
                        return false;
                    }

                    var full = SafePaths.Normalize(path);
                    return SafePaths.SegmentCount(full) >= 3
                        && !SafePaths.IsUnderWindows(full)
                        && !SafePaths.IsCriticalOrAncestor(full)
                        && !Win32.IsReparsePoint(full);
                }

                case "reg":
                    return IsAllowedRegKey(path);

                case "regvalue":
                    return !string.IsNullOrEmpty(i.Name)
                        && (IsAllowedRegKey(path) || s_runKey.IsMatch(path) || string.Equals(path, FirewallRules, StringComparison.OrdinalIgnoreCase));

                case "task":
                    return path.Length >= 2
                        && path[0] == '\\'
                        && !path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("..", StringComparison.Ordinal)
                        && !path.Contains('"');

                case "service":
                    return s_serviceName.IsMatch(path);

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool HasControl(string? text) => text is not null && text.Any(c => c < 0x20 || c == 0x7f);

    private static bool IsAllowedRegKey(string path)
    {
        var parts = path.Split('\\');
        if (parts.Contains("..") || parts.Contains("."))
        {
            return false;
        }

        var m = s_regKey.Match(path);
        return m.Success && !s_bannedSegments.Contains(m.Groups["seg"].Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The manifest bytes (UTF-8, no BOM) for these items.</summary>
    internal static byte[] BuildManifest(IEnumerable<Item> items) =>
        JsonSerializer.SerializeToUtf8Bytes(new { items = items.Select(i => new { kind = i.Kind, path = i.Path, name = i.Name, recycle = i.Recycle }) });

    /// <summary>Upper-case hex SHA-256 of the bytes.</summary>
    internal static string HashHex(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>The script lines that define <c>$CriticalRoots</c> (the user's critical folders) and <c>$WinDir</c> for <see cref="ValidationScript"/>.</summary>
    internal static string ScriptPreamble()
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var roots = string.Join(",", SafePaths.CriticalRoots().Select(Q));
        return $"$CriticalRoots = @({roots})\n$WinDir = {Q(Environment.GetFolderPath(Environment.SpecialFolder.Windows))}\n";
    }

    /// <summary>
    /// Removes the items in one elevated PowerShell process. Items that fail <see cref="IsAllowed"/> are refused (and count as failed).
    /// Returns the failed paths, or null when the user declined the prompt.
    /// </summary>
    public static List<string>? Remove(IReadOnlyList<Item> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var failed = new List<string>();
        var allowed = new List<Item>();
        foreach (var item in items)
        {
            if (IsAllowed(item))
            {
                allowed.Add(item);
            }
            else
            {
                failed.Add(item.Path);
                Host.Log($"Elevated removal refused an item of kind '{item.Kind}'");
            }
        }

        if (allowed.Count == 0)
        {
            return failed;
        }

        var dir = Path.Combine(Path.GetTempPath(), "trayloom-elevated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(dir, "manifest.json");
        var result = Path.Combine(dir, "result.json");
        try
        {
            var bytes = BuildManifest(allowed);
            File.WriteAllBytes(manifest, bytes);
            var hash = HashHex(bytes);
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $manifestPath = '{{manifest.Replace("'", "''")}}'
                $resultPath = '{{result.Replace("'", "''")}}'
                $expectedHash = '{{hash}}'
                {{ScriptPreamble()}}
                {{ValidationScript}}
                {{HashPrologue}}
                Add-Type -AssemblyName Microsoft.VisualBasic
                $m = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
                $failed = New-Object System.Collections.Generic.List[string]
                foreach ($i in @($m.items)) {
                  $p = [string]$i.path
                  try {
                    $kind = [string]$i.kind
                    if (-not (Test-Allowed $kind $p ([string]$i.name))) { throw 'denied' }
                    if ($kind -ceq 'reg') { Remove-Item -LiteralPath $p -Recurse -Force }
                    elseif ($kind -ceq 'regvalue') { Remove-ItemProperty -LiteralPath $p -Name ([WildcardPattern]::Escape([string]$i.name)) }
                    elseif ($kind -ceq 'task') { & "$env:SystemRoot\System32\schtasks.exe" /delete /tn $p /f | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'task' } }
                    elseif ($kind -ceq 'service') {
                      if (-not (Test-ServiceSafe $p)) { throw 'service' }
                      & "$env:SystemRoot\System32\sc.exe" delete $p | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'service' }
                    }
                    elseif ($kind -ceq 'file') {
                      $full = [IO.Path]::GetFullPath($p)
                      $item = Get-Item -LiteralPath $full -Force
                      if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'reparse' }
                      if ($i.recycle) {
                        if ($item.PSIsContainer) { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($full, 'OnlyErrorDialogs', 'SendToRecycleBin') }
                        else { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($full, 'OnlyErrorDialogs', 'SendToRecycleBin') }
                      } else { Remove-Item -LiteralPath $full -Recurse -Force }
                    }
                    else { throw 'kind' }
                  } catch { $failed.Add($p) }
                }
                if (-not (Test-DirSafe $resultPath)) { exit 4 }
                $json = ConvertTo-Json -InputObject @($failed.ToArray()) -Compress
                $out = [IO.File]::Open($resultPath, 'CreateNew', 'Write')
                try { $b = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json); $out.Write($b, 0, $b.Length) } finally { $out.Dispose() }
                """;
            if (!RunScript(script))
            {
                return null;
            }

            if (!File.Exists(result) || Win32.IsReparsePoint(result))
            {
                failed.AddRange(allowed.Select(i => i.Path));
                return failed;
            }

            var reported = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(result)) ?? [];
            var known = allowed.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
            failed.AddRange(reported.Where(known.Contains));
            return failed;
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // leave the temp folder
            }
        }
    }

    /// <summary>Terminates processes in one elevated PowerShell process after re-checking their creation time.</summary>
    public static bool Kill(IReadOnlyList<(int Pid, long Creation)> targets)
    {
        if (targets.Count == 0)
        {
            return true;
        }

        var rows = string.Join(",", targets.Select(t => $"@({t.Pid},{t.Creation})"));
        var script = $$"""
            foreach ($t in @({{rows}})) {
              try {
                $p = Get-Process -Id $t[0] -ErrorAction Stop
                if ($p.StartTime.ToFileTime() -eq $t[1]) { Stop-Process -Id $t[0] -Force -ErrorAction Stop }
              } catch { }
            }
            """;
        return RunScript(script);
    }

    private static bool RunScript(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var info = new ProcessStartInfo(powershell, $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(info);
            process?.WaitForExit(120_000);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches the current executable elevated and exits this process. Does nothing (returns false) unless
    /// <see cref="ElevationPolicy.CanOfferElevation"/>: from a per-user folder an elevated copy would run files an unprivileged
    /// process could have replaced.
    /// </summary>
    public static bool RelaunchAsAdministrator()
    {
        if (!ElevationPolicy.CanOfferElevation)
        {
            return false;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            // The running instance is single-instance: an elevated helper waits for it to quit, then starts the elevated copy.
            var script = $"Start-Sleep -Seconds 2; Start-Process -FilePath '{exe.Replace("'", "''")}'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            using var helper = Process.Start(new ProcessStartInfo(powershell, $"-NoProfile -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return helper is not null;
        }
        catch
        {
            return false;
        }
    }
}
