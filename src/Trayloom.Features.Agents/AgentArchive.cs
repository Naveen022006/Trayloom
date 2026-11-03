// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO.Compression;
using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>
/// Saved reading progress, so the next launch reads only what the agents wrote meanwhile. Private format
/// (<c>%LOCALAPPDATA%\Trayloom\AgentUsage\agent-usage.bin</c>, gzip JSON): any unreadable, foreign or other-format file reads as nothing and
/// logs are read from the start. Only counters, names and times are kept; OpenCode's data never is.
/// </summary>
internal static class AgentUsageArchive
{
    private const int Format = 1;

    public sealed class Contents
    {
        public int Version { get; set; } = Format;

        public List<AgentProvider> Providers { get; set; } = [];

        /// <summary>Which Claude lines were counted: <c>app</c> (the desktop app's only) or <c>all</c>. Older archives hold "" and are read again.</summary>
        public string ClaudeScope { get; set; } = "";

        public AgentUsageStore.Saved Store { get; set; } = new();

        public List<AgentLogCursor.SavedCursor> Cursors { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, IncludeFields = false };

    public static string Folder => Path.Combine(AgentLogRoot.LocalRoot, "AgentUsage");

    public static string FilePath => Path.Combine(Folder, "agent-usage.bin");

    public static bool Save(Contents contents)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            {
                JsonSerializer.Serialize(gzip, contents, Options);
            }

            File.Move(temp, FilePath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            return false;
        }
    }

    public static Contents? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var contents = JsonSerializer.Deserialize<Contents>(gzip, Options);
            return contents is { Version: Format } ? contents : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Remove()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Resumes where the last launch stopped, among the logs there now. A log gone or rewritten since is taken back, with whatever only it
    /// held. The limits read stay as saved.
    /// </summary>
    public static (AgentUsageStore Store, Dictionary<string, AgentLogCursor> Cursors) Resume(Contents contents, HashSet<string> found, DateTime horizon)
    {
        var store = AgentUsageStore.Restore(contents.Store);
        var cursors = new Dictionary<string, AgentLogCursor>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in contents.Cursors.Where(c => found.Contains(c.Path)))
        {
            if (AgentLogCursor.Restore(saved) is { } cursor)
            {
                cursors[saved.Path] = cursor;
            }
        }

        var gone = store.Files;
        gone.ExceptWith(cursors.Keys);
        var sharing = store.FilesSharingWith(gone);
        sharing.ExceptWith(gone);
        foreach (var file in sharing)
        {
            cursors.Remove(file);
        }

        gone.UnionWith(sharing);
        store.Forget(gone);
        // Prices may be newer than the ones the counts were saved with.
        store.Reprice();
        store.DropRecords(horizon);
        return (store, cursors);
    }
}
