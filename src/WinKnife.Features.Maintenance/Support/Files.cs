// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

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

namespace WinKnife.Features.Maintenance;

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
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

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

            foreach (var critical in CriticalRoots())
            {
                if (!string.IsNullOrEmpty(critical) && string.Equals(full, critical.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
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

    private static IEnumerable<string> CriticalRoots()
    {
        yield return Windows;
        yield return Path.Combine(Windows, "System32");
        yield return ProgramFiles;
        yield return ProgramFilesX86;
        yield return ProgramData;
        yield return @"C:\Users";
        yield return Profile;
        yield return AppData;
        yield return LocalAppData;
        yield return Path.Combine(LocalAppData, "Packages");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Path.Combine(Profile, "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        foreach (var key in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            yield return Env(key);
        }
    }

    /// <summary>Moves a file or folder to the Recycle Bin. Returns false when it could not be moved.</summary>
    public static bool Recycle(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return true;
            }

            var op = new Win32.SHFILEOPSTRUCT
            {
                wFunc = Win32.FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_NOERRORUI | Win32.FOF_SILENT),
            };
            var result = Win32.SHFileOperationW(ref op);
            return result == 0 && !op.fAnyOperationsAborted && !File.Exists(path) && !Directory.Exists(path);
        }
        catch
        {
            return false;
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
            return "Output";
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

/// <summary>Runs a short PowerShell script elevated (one UAC prompt) and reads the paths it reports back as failed.</summary>
internal static class Elevated
{
    /// <summary>One item of an elevated removal manifest.</summary>
    public sealed record Item(string Kind, string Path, string? Name = null, bool Recycle = false);

    /// <summary>
    /// Removes the items in one elevated PowerShell process. Returns the failed paths, or null when the user declined the prompt.
    /// Items are re-validated by the script (no reparse points).
    /// </summary>
    public static List<string>? Remove(IReadOnlyList<Item> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var dir = Path.Combine(Path.GetTempPath(), "winknife-elevated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(dir, "manifest.json");
        var result = Path.Combine(dir, "result.json");
        try
        {
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { items = items.Select(i => new { kind = i.Kind, path = i.Path, name = i.Name, recycle = i.Recycle }) }), new UTF8Encoding(false));
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $m = Get-Content -LiteralPath '{{manifest.Replace("'", "''")}}' -Raw | ConvertFrom-Json
                Add-Type -AssemblyName Microsoft.VisualBasic
                $failed = @()
                foreach ($i in $m.items) {
                  try {
                    if ($i.kind -eq 'reg') { Remove-Item -LiteralPath $i.path -Recurse -Force }
                    elseif ($i.kind -eq 'regvalue') { Remove-ItemProperty -LiteralPath $i.path -Name $i.name }
                    elseif ($i.kind -eq 'task') { schtasks.exe /delete /tn $i.path /f | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'task' } }
                    elseif ($i.kind -eq 'service') { sc.exe delete $i.path | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'service' } }
                    else {
                      $item = Get-Item -LiteralPath $i.path -Force
                      if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'reparse' }
                      if ($i.recycle) {
                        if ($item.PSIsContainer) { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($i.path, 'OnlyErrorDialogs', 'SendToRecycleBin') }
                        else { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($i.path, 'OnlyErrorDialogs', 'SendToRecycleBin') }
                      } else { Remove-Item -LiteralPath $i.path -Recurse -Force }
                    }
                  } catch { $failed += $i.path }
                }
                ConvertTo-Json -InputObject @($failed) -Compress | Set-Content -LiteralPath '{{result.Replace("'", "''")}}' -Encoding UTF8
                """;
            if (!RunScript(script))
            {
                return null;
            }

            if (!File.Exists(result))
            {
                return items.Select(i => i.Path).ToList();
            }

            var failed = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(result)) ?? [];
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
            var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
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

    /// <summary>Relaunches the current executable elevated and exits this process.</summary>
    public static bool RelaunchAsAdministrator()
    {
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
            using var helper = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -EncodedCommand {encoded}")
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
