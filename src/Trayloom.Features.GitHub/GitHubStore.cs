// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.GitHub;

/// <summary>What <c>cache.json</c> holds: the last snapshot and the notification ETag. Never a token or a header.</summary>
public sealed record GitHubCache(int V, string Login, DateTime FetchedAt, string? NotificationsEtag, int PollInterval, GitHubSnapshot Snapshot);

/// <summary><c>notch-github\cache.json</c>, written atomically so a crash cannot leave half a file.</summary>
internal sealed class GitHubStore(string directory)
{
    private string FilePath => Path.Combine(directory, "cache.json");

    public GitHubCache? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var cache = JsonSerializer.Deserialize<GitHubCache>(File.ReadAllText(FilePath), GhJson.Options);
            return cache is { V: 1 } ? cache : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(GitHubCache cache)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache, GhJson.Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cache is a convenience; the next poll writes it again
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
            File.Delete(FilePath + ".tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nothing more to do
        }
    }
}
