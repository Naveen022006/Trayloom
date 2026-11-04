// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Security.Cryptography;
using System.Text;

namespace Trayloom.Features.GitHub;

/// <summary>
/// The personal access token at <c>notch-github\token.bin</c>, encrypted with DPAPI for the current Windows account (spec 06 section 2.2 B).
/// The plaintext is handed out for one request and never kept in a field.
/// </summary>
internal sealed class GitHubTokenVault(string directory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Trayloom.GitHub.v1");

    private string FilePath => Path.Combine(directory, "token.bin");

    public bool Exists => File.Exists(FilePath);

    /// <summary>Encrypts and writes the token atomically.</summary>
    public void Save(string token)
    {
        Directory.CreateDirectory(directory);
        var plain = Encoding.UTF8.GetBytes(token);
        try
        {
            var blob = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, blob);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>The decrypted token, or null when there is none or it cannot be decrypted (another account or machine).</summary>
    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
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
            // leaves the encrypted blob; it is useless without this Windows account
        }
    }
}
