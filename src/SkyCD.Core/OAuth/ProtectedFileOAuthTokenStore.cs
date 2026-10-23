using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Atomic encrypted session files, kept outside catalogs, logs, and synchronized documents.</summary>
public sealed class ProtectedFileOAuthTokenStore(string directory, IOAuthDataProtector protector) : IOAuthTokenStore
{
    public async Task<OAuthSession?> ReadAsync(string namespaceId, string accountId, CancellationToken cancellationToken)
    {
        var path = SessionPath(namespaceId, accountId);
        try
        {
            var session = await ReadFileAsync(namespaceId, path, cancellationToken);
            if (session.AccountId != accountId)
                throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
            return session;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task WriteAsync(string namespaceId, OAuthSession session, CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session);
        byte[] encrypted;
        try { encrypted = protector.Protect(plaintext, Encoding.UTF8.GetBytes(namespaceId)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var path = SessionPath(namespaceId, session.AccountId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<string>> GetAccountsAsync(string namespaceId, CancellationToken cancellationToken)
    {
        var folder = NamespacePath(namespaceId);
        if (!Directory.Exists(folder))
            return [];
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.token"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await ReadFileAsync(namespaceId, path, cancellationToken);
            if (Path.GetFileName(path) != Hash(session.AccountId) + ".token")
                throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
            result.Add(session.AccountId);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    public Task DeleteAsync(string namespaceId, string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(SessionPath(namespaceId, accountId));
        return Task.CompletedTask;
    }

    private async Task<OAuthSession> ReadFileAsync(string namespaceId, string path, CancellationToken cancellationToken)
    {
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[] plaintext;
        try { plaintext = protector.Unprotect(encrypted, Encoding.UTF8.GetBytes(namespaceId)); }
        catch (CryptographicException) { throw new OAuthException(OAuthFailure.SecureStorageUnavailable); }
        try
        {
            var session = JsonSerializer.Deserialize<OAuthSession>(plaintext);
            if (session is null || string.IsNullOrWhiteSpace(session.AccountId) || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
            return session;
        }
        catch (JsonException) { throw new OAuthException(OAuthFailure.SecureStorageUnavailable); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private string NamespacePath(string namespaceId) => Path.Combine(directory, Hash(namespaceId));
    private string SessionPath(string namespaceId, string accountId) => Path.Combine(NamespacePath(namespaceId), Hash(accountId) + ".token");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
