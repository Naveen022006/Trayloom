// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>User-facing buckets of confirmed WhatsApp downloads. The ids are persisted: append, never rename.</summary>
internal enum WaCategory
{
    Image,
    Video,
    Audio,
    Document,
    Archive,
    Other,
}

internal enum WaLayout
{
    Flat,
    Category,
    Month,
}

internal enum WaDuplicateAction
{
    TrashNew,
    KeepBoth,
    ReplaceExisting,
}

/// <summary>
/// Pure classification and retention rules (Vorssaint's <c>WhatsAppDownloadSupport</c>). Every judgment of the destructive
/// boundary lives here so it can be tested without touching a real Downloads folder.
/// </summary>
internal static class WaRules
{
    public static readonly int[] AllowedRetentionDays = [1, 2, 7, 14, 30];

    public static readonly int[] AllowedOrganizerDelayMinutes = [1, 5, 15, 60];

    public static readonly TimeSpan OrganizerUndoLifetime = TimeSpan.FromDays(7);

    public static readonly WaCategory[] AllCategories = [WaCategory.Image, WaCategory.Video, WaCategory.Audio, WaCategory.Document, WaCategory.Archive, WaCategory.Other];

    public static readonly HashSet<WaCategory> DefaultCategories = [WaCategory.Image, WaCategory.Video, WaCategory.Audio];

    private static readonly HashSet<string> IncompleteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "download", "part", "partial", "crdownload", "tmp", "opdownload",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "7z", "bz", "bz2", "cab", "dmg", "gz", "iso", "rar", "tar", "tgz", "xz", "zip",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv", "doc", "docm", "docx", "epub", "key", "md", "numbers", "odp", "ods", "odt",
        "pages", "pdf", "ppt", "pptm", "pptx", "rtf", "tex", "txt", "xls", "xlsm", "xlsx",
    };

    // Windows has no UTType conformance: the media buckets come from the extension.
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jpe", "jfif", "png", "gif", "webp", "heic", "heif", "bmp", "tif", "tiff", "avif", "ico", "svg", "raw", "dng",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "mkv", "avi", "webm", "3gp", "3g2", "wmv", "mpg", "mpeg", "mts", "m2ts", "flv",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "aac", "wav", "ogg", "oga", "opus", "flac", "amr", "wma", "aif", "aiff", "caf", "mid", "midi",
    };

    // WhatsApp's own file names, used only when a file carries no Zone.Identifier stream at all.
    private static readonly Regex NamePattern = new(
        @"^WhatsApp (Image|Video|Audio|Ptt|Document|Sticker) \d{4}-\d{2}-\d{2} at .+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int SanitizedRetentionDays(int value) => Array.IndexOf(AllowedRetentionDays, value) >= 0 ? value : 7;

    public static int SanitizedOrganizerDelayMinutes(int value) => Array.IndexOf(AllowedOrganizerDelayMinutes, value) >= 0 ? value : 5;

    public static string Id(WaCategory category) => category switch
    {
        WaCategory.Image => "image",
        WaCategory.Video => "video",
        WaCategory.Audio => "audio",
        WaCategory.Document => "document",
        WaCategory.Archive => "archive",
        _ => "other",
    };

    public static string Id(WaLayout layout) => layout switch
    {
        WaLayout.Category => "category",
        WaLayout.Month => "month",
        _ => "flat",
    };

    public static string Id(WaDuplicateAction action) => action switch
    {
        WaDuplicateAction.KeepBoth => "keepBoth",
        WaDuplicateAction.ReplaceExisting => "replaceExisting",
        _ => "trashNew",
    };

    public static WaLayout ParseLayout(string? raw) => raw switch
    {
        "category" => WaLayout.Category,
        "month" => WaLayout.Month,
        _ => WaLayout.Flat,
    };

    public static WaDuplicateAction ParseDuplicateAction(string? raw) => raw switch
    {
        "keepBoth" => WaDuplicateAction.KeepBoth,
        "replaceExisting" => WaDuplicateAction.ReplaceExisting,
        _ => WaDuplicateAction.TrashNew,
    };

    public static string EncodedCategories(IEnumerable<WaCategory> categories)
    {
        var set = new HashSet<WaCategory>(categories);
        return string.Join(",", AllCategories.Where(set.Contains).Select(Id));
    }

    /// <summary>Comma list to a set. A null value is the default selection; an empty string is "none".</summary>
    public static HashSet<WaCategory> DecodedCategories(string? raw)
    {
        if (raw is null)
        {
            return [.. DefaultCategories];
        }

        var result = new HashSet<WaCategory>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var category in AllCategories)
            {
                if (Id(category) == part)
                {
                    result.Add(category);
                }
            }
        }

        return result;
    }

    public static bool IsIncompleteFile(string extension) => IncompleteExtensions.Contains(extension.TrimStart('.'));

    public static bool IsDirectChild(string candidate, string root)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(candidate));
            return parent is not null && string.Equals(parent.TrimEnd('\\'), Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDescendant(string candidate, string root)
    {
        try
        {
            var full = Path.GetFullPath(candidate).TrimEnd('\\');
            var top = Path.GetFullPath(root).TrimEnd('\\');
            return full.Length > top.Length && full.StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsStableForOrganization(DateTime downloadedAt, DateTime modifiedAt, DateTime now, int delayMinutes)
    {
        var delay = TimeSpan.FromMinutes(SanitizedOrganizerDelayMinutes(delayMinutes));
        var newest = downloadedAt > modifiedAt ? downloadedAt : modifiedAt;
        return now - newest >= delay;
    }

    public static bool OrganizerUndoIsValid(DateTime createdAt, DateTime now)
    {
        var age = now - createdAt;
        return age >= TimeSpan.Zero && age < OrganizerUndoLifetime;
    }

    public static string OrganizerCategoryFolder(WaCategory category) => category switch
    {
        WaCategory.Image => Loc.T("Images"),
        WaCategory.Video => Loc.T("Videos"),
        WaCategory.Audio => "Audio",
        WaCategory.Document => Loc.T("Documents"),
        WaCategory.Archive => Loc.T("Archives"),
        _ => Loc.T("Other"),
    };

    public static string[] OrganizerRelativeComponents(WaLayout layout, WaCategory category, DateTime date) => layout switch
    {
        WaLayout.Category => [OrganizerCategoryFolder(category)],
        WaLayout.Month => [date.Year.ToString("0000", CultureInfo.InvariantCulture), date.Month.ToString("00", CultureInfo.InvariantCulture)],
        _ => [],
    };

    /// <summary>Archive and document extensions win; then the media buckets; everything else is "Other".</summary>
    public static WaCategory CategoryOf(string extension)
    {
        var ext = extension.TrimStart('.');
        if (ArchiveExtensions.Contains(ext))
        {
            return WaCategory.Archive;
        }

        if (DocumentExtensions.Contains(ext))
        {
            return WaCategory.Document;
        }

        if (ImageExtensions.Contains(ext))
        {
            return WaCategory.Image;
        }

        if (VideoExtensions.Contains(ext))
        {
            return WaCategory.Video;
        }

        return AudioExtensions.Contains(ext) ? WaCategory.Audio : WaCategory.Other;
    }

    /// <summary>Old enough only when both the download time and the latest edit are beyond the window.</summary>
    public static bool IsOldEnough(DateTime downloadedAt, DateTime modifiedAt, DateTime now, int retentionDays)
    {
        var cutoff = now.AddDays(-SanitizedRetentionDays(retentionDays));
        return downloadedAt <= cutoff && modifiedAt <= cutoff;
    }

    public static bool IsEligibleForRules(WaCategory category, DateTime downloadedAt, DateTime modifiedAt, DateTime now, int retentionDays, ISet<WaCategory> enabled) =>
        enabled.Contains(category) && IsOldEnough(downloadedAt, modifiedAt, now, retentionDays);

    public static bool IsEligibleForAutomaticCleanup(WaCategory category, DateTime downloadedAt, DateTime modifiedAt, DateTime now, int retentionDays, ISet<WaCategory> enabled, bool includeExisting, DateTime? automaticStartDate)
    {
        if (!IsEligibleForRules(category, downloadedAt, modifiedAt, now, retentionDays, enabled))
        {
            return false;
        }

        if (includeExisting)
        {
            return true;
        }

        return automaticStartDate is not null && downloadedAt >= automaticStartDate.Value;
    }

    /// <summary>The next 09:00 strictly after <paramref name="after"/>.</summary>
    public static DateTime NextAutomaticCheck(DateTime after)
    {
        var today = new DateTime(after.Year, after.Month, after.Day, 9, 0, 0, DateTimeKind.Local);
        var fire = today > after ? today : today.AddDays(1);
        return TimeZoneInfo.Local.IsInvalidTime(fire) ? fire.AddHours(1) : fire;
    }

    /// <summary>True when today's 09:00 has passed and the last pass was before it (a never-run tool waits for the next 09:00).</summary>
    public static bool MissedAutomaticCheck(DateTime now, DateTime? lastRun)
    {
        if (lastRun is null)
        {
            return false;
        }

        var today = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, DateTimeKind.Local);
        return now >= today && lastRun.Value < today;
    }

    /// <summary>The WhatsApp hosts that confirm a download: whatsapp.com, whatsapp.net and their sub-domains.</summary>
    public static bool IsWhatsAppHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        return h is "whatsapp.com" or "whatsapp.net" || h.EndsWith(".whatsapp.com", StringComparison.Ordinal) || h.EndsWith(".whatsapp.net", StringComparison.Ordinal);
    }

    /// <summary>True when a Zone.Identifier text names a WhatsApp host as the HostUrl or the ReferrerUrl.</summary>
    public static bool ZoneTextIsWhatsApp(string zoneText)
    {
        foreach (var raw in zoneText.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            if (!key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase) && !key.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Uri.TryCreate(line[(eq + 1)..].Trim(), UriKind.Absolute, out var uri) && IsWhatsAppHost(uri.Host))
            {
                return true;
            }
        }

        return false;
    }

    public static bool NameLooksLikeWhatsApp(string fileName) => NamePattern.IsMatch(fileName);
}

/// <summary>Windows file-system layer of the tool: known folders, the Zone.Identifier stream, identities, hashing.</summary>
internal static class WaFiles
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);

    /// <summary>Test hook: when set, replaces the Downloads folder (used by the test harness with scratch folders only).</summary>
    internal static string? DownloadsOverride { get; set; }

    /// <summary>The user's Downloads folder (honours a redirected one).</summary>
    public static string DownloadsPath()
    {
        if (DownloadsOverride is { Length: > 0 } forced)
        {
            return Path.GetFullPath(forced);
        }

        try
        {
            if (SHGetKnownFolderPath(DownloadsFolderId, 0, nint.Zero, out var pointer) == 0 && pointer != nint.Zero)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(pointer);
                    if (!string.IsNullOrEmpty(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }
        catch
        {
            // fall through to the profile folder
        }

        return Path.Combine(FileOps.Profile, "Downloads");
    }

    /// <summary>
    /// WhatsApp Desktop's packaged private store(s), located read-only. They are never scanned, moved from or deleted
    /// from (the container is the app's database); this only lets tests and diagnostics prove it.
    /// </summary>
    public static IReadOnlyList<string> PackageStores()
    {
        var list = new List<string>();
        try
        {
            var packages = Path.Combine(FileOps.LocalAppData, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var dir in Directory.EnumerateDirectories(packages, "5319275A.WhatsAppDesktop_*"))
                {
                    list.Add(dir);
                }
            }
        }
        catch
        {
            // not installed or unreadable
        }

        return list;
    }

    /// <summary>True when the path lies inside a WhatsApp package folder.</summary>
    public static bool IsInsidePackageStore(string path) => PackageStores().Any(store => FileOps.IsUnder(path, store));

    /// <summary>The content of the Zone.Identifier stream, or null when the file has none.</summary>
    public static string? ReadZoneIdentifier(string path)
    {
        try
        {
            using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether Windows itself attributes the file to WhatsApp: a Zone.Identifier naming a WhatsApp host; when the stream is
    /// absent altogether, WhatsApp's own file-name pattern. A stream that names another site is never WhatsApp.
    /// </summary>
    public static bool IsConfirmedWhatsApp(string path)
    {
        var zone = ReadZoneIdentifier(path);
        if (zone is null)
        {
            return WaRules.NameLooksLikeWhatsApp(Path.GetFileName(path));
        }

        return WaRules.ZoneTextIsWhatsApp(zone);
    }

    public static string? Fingerprint(string path) => Win32.FileIdentity(path);

    /// <summary>Volume part of a <see cref="Fingerprint"/>.</summary>
    public static string? VolumeOf(string path)
    {
        var id = Win32.FileIdentity(path);
        var dash = id?.IndexOf('-') ?? -1;
        return dash > 0 ? id![..dash] : null;
    }

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
    }

    public static bool IsUsableFile(FileInfo info)
    {
        try
        {
            var attrs = info.Attributes;
            return info.Exists
                && (attrs & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Device)) == 0
                && !WaRules.IsIncompleteFile(info.Extension);
        }
        catch
        {
            return false;
        }
    }
}
