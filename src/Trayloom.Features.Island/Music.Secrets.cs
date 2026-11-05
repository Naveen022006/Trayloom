// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>
/// Sign-ins and API keys of the music services, encrypted for the current Windows user (DPAPI) in small files outside settings.json,
/// so they stay out of settings exports. Writes are immediate and atomic.
/// </summary>
internal sealed class SecretStore
{
    private readonly string _directory;
    private readonly ILog? _log;

    public SecretStore(string directory, ILog? log = null)
    {
        _directory = directory;
        _log = log;
    }

    /// <summary>The store under <c>%APPDATA%\Trayloom\music</c> (or the TRAYLOOM_DATA folder).</summary>
    public static SecretStore Default(ILog? log = null) => new(Path.Combine(AppPaths.Root, "music"), log);

    /// <summary>Reads and decrypts <paramref name="name"/>; a file that cannot be decrypted or is not JSON is deleted and reported as missing; an I/O error keeps it.</summary>
    public bool TryRead(string name, out string json)
    {
        json = "";
        var path = PathOf(name);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(name), DataProtectionScope.CurrentUser));
            using (JsonDocument.Parse(plain))
            {
            }

            json = plain;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Never log the exception text: it must not carry anything about the file's content.
            if (ShouldDelete(ex))
            {
                _log?.Warn("[island] Music: stored sign-in unreadable, cleared (" + name + ")");
                Delete(name);
            }
            else
            {
                // A locked file or a denied read says nothing about the sign-in itself, so it is kept for the next try.
                _log?.Warn("[island] Music: stored sign-in could not be read, kept (" + name + ")");
            }

            return false;
        }
    }

    /// <summary>
    /// True only when the file itself is proven bad (it cannot be decrypted or is not JSON). An I/O error, a denied access or a
    /// bad argument must never delete a sign-in.
    /// </summary>
    internal static bool ShouldDelete(Exception e) => e is CryptographicException or JsonException;

    /// <summary>Encrypts and writes <paramref name="json"/> immediately (temp file, then replace).</summary>
    public void Write(string name, string json)
    {
        Directory.CreateDirectory(_directory);
        var path = PathOf(name);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy(name), DataProtectionScope.CurrentUser));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deletes <paramref name="name"/> if present.</summary>
    public void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Warn("[island] Music: could not delete stored sign-in (" + name + ")");
        }
    }

    private string PathOf(string name) => Path.Combine(_directory, name + ".bin");

    private static byte[] Entropy(string name) => Encoding.UTF8.GetBytes("Trayloom.Music.v1:" + name);
}

/// <summary>A Spotify sign-in as stored in <c>spotify.bin</c>.</summary>
internal sealed record TokenSet(string ClientId, string Access, string Refresh, DateTime ExpiresAtUtc, string Scope, bool? Premium, string DisplayName)
{
    /// <summary>True when the access token has expired or will within a minute.</summary>
    public bool NeedsRefresh(DateTime nowUtc) => ExpiresAtUtc - nowUtc <= TimeSpan.FromSeconds(60);

    /// <summary>Serializes as <c>{"v":1,...}</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["v"] = 1,
        ["clientId"] = ClientId,
        ["access"] = Access,
        ["refresh"] = Refresh,
        ["expiresAtUtc"] = ExpiresAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["scope"] = Scope,
        ["premium"] = Premium,
        ["displayName"] = DisplayName,
    });

    /// <summary>Parses <see cref="ToJson"/> output; null for anything else, including another version or missing fields.</summary>
    public static TokenSet? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
            {
                return null;
            }

            var client = Str(root, "clientId");
            var access = Str(root, "access");
            var refresh = Str(root, "refresh");
            if (client is null || access is null || refresh is null
                || Str(root, "expiresAtUtc") is not { } stamp
                || !DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expires))
            {
                return null;
            }

            bool? premium = root.TryGetProperty("premium", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
            return new TokenSet(client, access, refresh, expires, Str(root, "scope") ?? "", premium, Str(root, "displayName") ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
