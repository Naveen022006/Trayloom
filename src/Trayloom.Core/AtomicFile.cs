// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;

namespace Trayloom.Core;

/// <summary>
/// Crash-safe file writes: the content goes to a temp file in the same folder, is flushed to disk and then swapped in, so a reader
/// (or a crash) sees either the old file or the new one, never half of one.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/> atomically (default encoding UTF-8 without BOM). A symlinked target is
    /// written through to its final target. The temp file is deleted on failure.
    /// </summary>
    public static void WriteAllText(string path, string text, Encoding? encoding = null)
    {
        var target = ResolveTarget(Path.GetFullPath(path));
        var folder = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteTemp(tmp, text, encoding);
            try
            {
                if (File.Exists(target))
                {
                    File.Replace(tmp, target, null);
                }
                else
                {
                    File.Move(tmp, target);
                }
            }
            catch (FileNotFoundException) when (!File.Exists(target) && File.Exists(tmp))
            {
                File.Move(tmp, target);
            }
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }
    }

    /// <summary>
    /// Creates <paramref name="path"/> with <paramref name="text"/> only if it does not exist yet. Returns false, changing nothing,
    /// when it already exists.
    /// </summary>
    public static bool TryCreateNew(string path, string text)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) || Directory.Exists(full))
        {
            return false;
        }

        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteTemp(tmp, text, null);
            File.Move(tmp, full, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(full))
        {
            DeleteQuietly(tmp);
            return false;
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }
    }

    private static void WriteTemp(string tmp, string text, Encoding? encoding)
    {
        var bytes = (encoding ?? new UTF8Encoding(false)).GetBytes(text);
        using var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static string ResolveTarget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(true) is { } final)
            {
                return final.FullName;
            }
        }
        catch
        {
            // not a link, or unreadable: write to the path itself
        }

        return path;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // leave it
        }
    }
}
